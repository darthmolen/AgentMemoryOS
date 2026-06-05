namespace AgentMemoryOS.Tests.Pipeline;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Embeddings;
using AgentMemoryOS.Pipeline;
using AgentMemoryOS.Stores;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class MemoryReconcilerTests
{
    [TestMethod]
    public async Task PublishedObservationBecomesFactAndVectorHit()
    {
        var queue = new ObservationChannel();
        var sink = new ChannelObservationSink(queue);
        var facts = new InMemoryFactStore(TimeProvider.System);
        var vectors = new InMemoryVectorRecall();
        using var embedder = new HashingEmbeddingGenerator();
        using var reconciler = new MemoryReconciler(queue, facts, vectors, embedder, NullLogger<MemoryReconciler>.Instance);

        await reconciler.StartAsync(CancellationToken.None);
        try
        {
            await sink.PublishAsync(new ObservationCaptured(
                MemoryScope.Default,
                new Observation("Repo gates on a React score below 50", DateTimeOffset.UtcNow)));

            var recalledFacts = await WaitForAsync(async () =>
            {
                var hits = await facts.RecallAsync(MemoryScope.Default, "repo react score", minTrust: 0.0);
                return hits.Count > 0 ? hits : null;
            });

            Assert.IsNotNull(recalledFacts);

            var query = (await embedder.GenerateAsync(["Repo gates on a React score below 50"])).Single().Vector;
            var vectorHits = await vectors.RecallAsync(MemoryScope.Default, query, minScore: 0.5);
            Assert.IsTrue(vectorHits.Count > 0);
        }
        finally
        {
            await reconciler.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task DuplicateObservationsRaiseTrustThroughReconciler()
    {
        var queue = new ObservationChannel();
        var sink = new ChannelObservationSink(queue);
        var facts = new InMemoryFactStore(TimeProvider.System);
        var vectors = new InMemoryVectorRecall();
        using var embedder = new HashingEmbeddingGenerator();
        using var reconciler = new MemoryReconciler(queue, facts, vectors, embedder, NullLogger<MemoryReconciler>.Instance);

        await reconciler.StartAsync(CancellationToken.None);
        try
        {
            var observation = new Observation("Team treats rule Z as comment only", DateTimeOffset.UtcNow);
            await sink.PublishAsync(new ObservationCaptured(MemoryScope.Default, observation));
            await sink.PublishAsync(new ObservationCaptured(MemoryScope.Default, observation));

            var recalled = await WaitForAsync(async () =>
            {
                var hits = await facts.RecallAsync(MemoryScope.Default, "rule Z comment", minTrust: 0.0);
                return hits.Count > 0 && hits[0].OccurrenceCount == 2 ? hits : null;
            });

            Assert.IsNotNull(recalled);
            Assert.IsTrue(recalled[0].Trust > 0.5);
        }
        finally
        {
            await reconciler.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<T?> WaitForAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(20);
        }

        return null;
    }
}
