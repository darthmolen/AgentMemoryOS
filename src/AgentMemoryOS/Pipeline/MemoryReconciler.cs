using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentMemoryOS.Pipeline;

/// <summary>
/// Drains the <see cref="ObservationChannel"/> off the request path and reconciles each
/// observation into durable memory: it embeds and indexes the content for semantic recall
/// (L5) and upsert-merges it into the trust-scored fact store (L3). Failures fail open — a
/// single bad observation is logged and skipped without stopping the loop.
/// </summary>
public sealed partial class MemoryReconciler : BackgroundService
{
    private readonly ObservationChannel queue;
    private readonly IFactStore facts;
    private readonly IVectorRecall vectors;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embedder;
    private readonly ILogger<MemoryReconciler> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryReconciler"/> class.
    /// </summary>
    /// <param name="queue">The shared queue of captured observations to drain.</param>
    /// <param name="facts">The fact store that observations are merged into.</param>
    /// <param name="vectors">The vector store that observations are indexed into.</param>
    /// <param name="embedder">The embedding generator used to vectorize content.</param>
    /// <param name="logger">The logger used to report reconciliation failures.</param>
    public MemoryReconciler(
        ObservationChannel queue,
        IFactStore facts,
        IVectorRecall vectors,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        ILogger<MemoryReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(logger);

        this.queue = queue;
        this.facts = facts;
        this.vectors = vectors;
        this.embedder = embedder;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var captured in this.queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await this.ReconcileAsync(captured, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.LogReconcileFailed(captured.Scope.TemplateId, ex);
            }
        }
    }

    private async Task ReconcileAsync(ObservationCaptured captured, CancellationToken cancellationToken)
    {
        var content = captured.Observation.Content;
        var embeddings = await this.embedder.GenerateAsync([content], cancellationToken: cancellationToken);
        var record = new MemoryRecord(MemoryText.NormalizeKey(content), content, embeddings[0].Vector);

        await this.vectors.IndexAsync(captured.Scope, record, cancellationToken);
        await this.facts.UpsertObservationAsync(captured.Scope, captured.Observation, cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "Failed to reconcile an observation for template {TemplateId}.")]
    private partial void LogReconcileFailed(string templateId, Exception exception);
}
