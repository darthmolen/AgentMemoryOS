using System.Text;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Recall;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentMemoryOS;

/// <summary>
/// A Microsoft Agent Framework <see cref="AIContextProvider"/> that gives an agent tiered
/// memory: it injects an always-on workspace plus gated, deduped recall before each
/// invocation, and captures durable observations afterwards. Capture is asynchronous — the
/// hot path only extracts and publishes observations; a background reconciler turns them into
/// trust-scored facts and vector-indexed recall.
/// </summary>
/// <remarks>
/// Every store call is keyed by a required <see cref="MemoryScope"/>, so swapping the backing
/// stores for a template-partitioned shared implementation later leaves this provider's logic
/// unchanged. Memory is best-effort: store and extractor failures are logged and never
/// propagate into the agent invocation.
/// </remarks>
public sealed partial class TieredMemoryProvider : AIContextProvider
{
    private static readonly HashSet<string> NoSeenKeys = new(StringComparer.Ordinal);

    private readonly MemoryScope scope;
    private readonly IChatClient extractor;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embedder;
    private readonly IWorkspaceStore workspace;
    private readonly IFactStore facts;
    private readonly IVectorRecall vectors;
    private readonly IObservationSink sink;
    private readonly TieredMemoryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<TieredMemoryProvider> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TieredMemoryProvider"/> class.
    /// </summary>
    /// <param name="scope">The required partition all reads and writes are keyed by.</param>
    /// <param name="extractor">The chat client used to extract observations from an exchange.</param>
    /// <param name="embedder">The embedding generator used to vectorize recall queries.</param>
    /// <param name="workspace">The always-on workspace store (L1).</param>
    /// <param name="facts">The trust-scored fact store (L3).</param>
    /// <param name="vectors">The semantic vector store (L5).</param>
    /// <param name="sink">The sink that captured observations are published to.</param>
    /// <param name="options">The recall and extraction options.</param>
    /// <param name="timeProvider">The time source used to stamp observations.</param>
    /// <param name="logger">The logger used to report fail-open memory errors.</param>
    public TieredMemoryProvider(
        MemoryScope scope,
        IChatClient extractor,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        IWorkspaceStore workspace,
        IFactStore facts,
        IVectorRecall vectors,
        IObservationSink sink,
        TieredMemoryOptions options,
        TimeProvider timeProvider,
        ILogger<TieredMemoryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.scope = scope;
        this.extractor = extractor;
        this.embedder = embedder;
        this.workspace = workspace;
        this.facts = facts;
        this.vectors = vectors;
        this.sink = sink;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ValueTask<AIContext>(this.BuildContextAsync(LastUserText(context.AIContext.Messages), cancellationToken));
    }

    /// <inheritdoc />
    protected override ValueTask StoreAIContextAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ValueTask(this.CaptureAsync(LastUserText(context.RequestMessages), BuildTranscript(context), cancellationToken));
    }

    internal async Task<AIContext> BuildContextAsync(string? query, CancellationToken cancellationToken)
    {
        var instructions = new StringBuilder();

        var loadedWorkspace = await this.LoadWorkspaceAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(loadedWorkspace))
        {
            instructions.Append(loadedWorkspace);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var recalled = await this.RecallAsync(query, cancellationToken);
            if (recalled.Count > 0)
            {
                if (instructions.Length > 0)
                {
                    instructions.Append("\n\n");
                }

                instructions.Append("Relevant remembered context:");
                foreach (var item in recalled)
                {
                    instructions.Append("\n- ").Append(item.Content);
                }
            }
        }

        return instructions.Length > 0
            ? new AIContext { Instructions = instructions.ToString() }
            : new AIContext();
    }

    internal async Task CaptureAsync(string? userText, string transcript, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userText) || MemoryGate.IsTrivial(userText))
        {
            return;
        }

        List<string> observations;
        try
        {
            observations = await this.ExtractAsync(transcript, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogExtractionFailed(this.scope.TemplateId, ex);
            return;
        }

        var now = this.timeProvider.GetUtcNow();
        foreach (var text in observations)
        {
            await this.sink.PublishAsync(new ObservationCaptured(this.scope, new Observation(text, now)), cancellationToken);
        }
    }

    private static string? LastUserText(IEnumerable<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return null;
        }

        string? last = null;
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
            {
                last = message.Text;
            }
        }

        return last;
    }

    private static string BuildTranscript(InvokedContext context)
    {
        var user = LastUserText(context.RequestMessages) ?? string.Empty;
        var assistant = context.ResponseMessages is null
            ? string.Empty
            : string.Join("\n", context.ResponseMessages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text));

        return $"User: {user}\nAssistant: {assistant}";
    }

    private static List<string> ParseObservations(string text)
    {
        var observations = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('-', '*', '•', ' ').Trim();
            if (line.Length == 0 || string.Equals(line, "none", StringComparison.OrdinalIgnoreCase) || MemoryGate.IsTrivial(line))
            {
                continue;
            }

            observations.Add(line);
        }

        return observations;
    }

    private async Task<string?> LoadWorkspaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this.workspace.LoadAsync(this.scope, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogRecallFailed("workspace", ex);
            return null;
        }
    }

    private async Task<IReadOnlyList<ScoredCandidate>> RecallAsync(string query, CancellationToken cancellationToken)
    {
        var candidates = new List<ScoredCandidate>();

        try
        {
            var recalledFacts = await this.facts.RecallAsync(this.scope, query, this.options.MinTrust, cancellationToken);
            candidates.AddRange(recalledFacts.Select(fact => new ScoredCandidate(fact.Content, fact.Trust, RecallSource.Fact, null)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogRecallFailed("facts", ex);
        }

        try
        {
            var embedding = await this.embedder.GenerateAsync([query], cancellationToken: cancellationToken);
            var hits = await this.vectors.RecallAsync(this.scope, embedding[0].Vector, this.options.MinVectorScore, cancellationToken);
            candidates.AddRange(hits.Select(hit => new ScoredCandidate(hit.Content, hit.Score, RecallSource.Vector, null)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogRecallFailed("vector", ex);
        }

        var deduped = MemoryGate.Dedup(candidates, NoSeenKeys, this.options.CosineDedupThreshold).Kept;
        return MemoryGate.Gate(deduped, 0.0, this.options.MaxRecallItems);
    }

    private async Task<List<string>> ExtractAsync(string transcript, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, this.options.ExtractionInstructions),
            new(ChatRole.User, transcript),
        };

        var response = await this.extractor.GetResponseAsync(messages, options: null, cancellationToken);
        return ParseObservations(response.Text);
    }

    [LoggerMessage(LogLevel.Warning, "Memory recall failed for the {Layer} layer; continuing with reduced context.")]
    private partial void LogRecallFailed(string layer, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Observation extraction failed for template {TemplateId}; skipping capture.")]
    private partial void LogExtractionFailed(string templateId, Exception exception);
}
