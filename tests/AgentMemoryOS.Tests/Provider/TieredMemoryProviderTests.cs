namespace AgentMemoryOS.Tests.Provider;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentMemoryOS;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Embeddings;
using AgentMemoryOS.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

internal sealed class FakeChatClient : IChatClient
{
    private readonly string responseText;

    public FakeChatClient(string responseText)
    {
        this.responseText = responseText;
    }

    public int CallCount { get; private set; }

    public bool ThrowOnResponse { get; set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.CallCount++;
        if (this.ThrowOnResponse)
        {
            throw new InvalidOperationException("extractor unavailable");
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, this.responseText)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

internal sealed class RecordingSink : IObservationSink
{
    public List<ObservationCaptured> Published { get; } = [];

    public ValueTask PublishAsync(ObservationCaptured observation, CancellationToken cancellationToken = default)
    {
        this.Published.Add(observation);
        return ValueTask.CompletedTask;
    }
}

[TestClass]
public sealed class TieredMemoryProviderTests
{
    [TestMethod]
    public async Task BuildContextInjectsWorkspaceAndRecalledFacts()
    {
        var workspace = new InMemoryWorkspaceStore();
        workspace.Set(MemoryScope.Default, "# Team workspace");
        var facts = new InMemoryFactStore(TimeProvider.System);
        await facts.UpsertObservationAsync(MemoryScope.Default, new Observation("Repo gates on a React score below 50", DateTimeOffset.UtcNow));
        using var embedder = new HashingEmbeddingGenerator();
        using var extractor = new FakeChatClient("NONE");
        var provider = CreateProvider(extractor, workspace, facts, new InMemoryVectorRecall(), new RecordingSink(), embedder);

        var context = await provider.BuildContextAsync("what does the repo gate on for the react score", CancellationToken.None);

        Assert.IsNotNull(context.Instructions);
        StringAssert.Contains(context.Instructions, "Team workspace");
        StringAssert.Contains(context.Instructions, "React score");
    }

    [TestMethod]
    public async Task BuildContextWithNoQueryReturnsWorkspaceOnly()
    {
        var workspace = new InMemoryWorkspaceStore();
        workspace.Set(MemoryScope.Default, "# Only workspace");
        using var embedder = new HashingEmbeddingGenerator();
        using var extractor = new FakeChatClient("NONE");
        var provider = CreateProvider(extractor, workspace, new InMemoryFactStore(TimeProvider.System), new InMemoryVectorRecall(), new RecordingSink(), embedder);

        var context = await provider.BuildContextAsync(null, CancellationToken.None);

        Assert.AreEqual("# Only workspace", context.Instructions);
    }

    [TestMethod]
    public async Task CaptureSkipsTrivialUserText()
    {
        using var embedder = new HashingEmbeddingGenerator();
        using var extractor = new FakeChatClient("Some durable fact");
        var sink = new RecordingSink();
        var provider = CreateProvider(extractor, new InMemoryWorkspaceStore(), new InMemoryFactStore(TimeProvider.System), new InMemoryVectorRecall(), sink, embedder);

        await provider.CaptureAsync("thanks", "User: thanks\nAssistant: you're welcome", CancellationToken.None);

        Assert.AreEqual(0, sink.Published.Count);
        Assert.AreEqual(0, extractor.CallCount);
    }

    [TestMethod]
    public async Task CapturePublishesExtractedObservations()
    {
        using var embedder = new HashingEmbeddingGenerator();
        using var extractor = new FakeChatClient("Repo gates on React score 50\n- Team treats rule Z as comment-only");
        var sink = new RecordingSink();
        var provider = CreateProvider(extractor, new InMemoryWorkspaceStore(), new InMemoryFactStore(TimeProvider.System), new InMemoryVectorRecall(), sink, embedder);

        await provider.CaptureAsync("the repo gates on react score 50", "User: ...\nAssistant: noted", CancellationToken.None);

        Assert.AreEqual(2, sink.Published.Count);
    }

    [TestMethod]
    public async Task CaptureFailsOpenWhenExtractorThrows()
    {
        using var embedder = new HashingEmbeddingGenerator();
        using var extractor = new FakeChatClient("ignored") { ThrowOnResponse = true };
        var sink = new RecordingSink();
        var provider = CreateProvider(extractor, new InMemoryWorkspaceStore(), new InMemoryFactStore(TimeProvider.System), new InMemoryVectorRecall(), sink, embedder);

        await provider.CaptureAsync("a substantive statement worth remembering", "transcript", CancellationToken.None);

        Assert.AreEqual(0, sink.Published.Count);
    }

    private static TieredMemoryProvider CreateProvider(
        IChatClient extractor,
        IWorkspaceStore workspace,
        IFactStore facts,
        IVectorRecall vectors,
        IObservationSink sink,
        HashingEmbeddingGenerator embedder)
    {
        var options = new TieredMemoryOptions { MinTrust = 0.0, MinVectorScore = 0.5 };
        return new TieredMemoryProvider(
            MemoryScope.Default,
            extractor,
            embedder,
            workspace,
            facts,
            vectors,
            sink,
            options,
            TimeProvider.System,
            NullLogger<TieredMemoryProvider>.Instance);
    }
}
