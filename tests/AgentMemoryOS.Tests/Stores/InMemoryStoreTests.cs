namespace AgentMemoryOS.Tests.Stores;

using System;
using System.Linq;
using System.Threading.Tasks;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Embeddings;
using AgentMemoryOS.Recall;
using AgentMemoryOS.Stores;

[TestClass]
public sealed class HashingEmbeddingGeneratorTests
{
    [TestMethod]
    public async Task SameTextProducesIdenticalVectors()
    {
        using var generator = new HashingEmbeddingGenerator();

        var first = (await generator.GenerateAsync(["repo gates on score"])).Single().Vector;
        var second = (await generator.GenerateAsync(["repo gates on score"])).Single().Vector;

        Assert.AreEqual(1.0, VectorMath.CosineSimilarity(first, second), 1e-6);
    }

    [TestMethod]
    public async Task UnrelatedTextProducesDissimilarVectors()
    {
        using var generator = new HashingEmbeddingGenerator();

        var first = (await generator.GenerateAsync(["alpha beta gamma"])).Single().Vector;
        var second = (await generator.GenerateAsync(["completely unrelated vocabulary"])).Single().Vector;

        Assert.IsTrue(VectorMath.CosineSimilarity(first, second) < 0.5);
    }
}

[TestClass]
public sealed class InMemoryWorkspaceStoreTests
{
    [TestMethod]
    public async Task ReturnsSeededTextForScope()
    {
        var store = new InMemoryWorkspaceStore();
        store.Set(MemoryScope.Default, "# Workspace");

        Assert.AreEqual("# Workspace", await store.LoadAsync(MemoryScope.Default));
    }

    [TestMethod]
    public async Task ReturnsNullForUnknownScope()
    {
        var store = new InMemoryWorkspaceStore();

        Assert.IsNull(await store.LoadAsync(new MemoryScope("unknown")));
    }
}

[TestClass]
public sealed class InMemoryFactStoreTests
{
    [TestMethod]
    public async Task UpsertThenRecallReturnsFact()
    {
        var store = new InMemoryFactStore(TimeProvider.System);
        await store.UpsertObservationAsync(MemoryScope.Default, new Observation("Repo gates on score 50", DateTimeOffset.UtcNow));

        var facts = await store.RecallAsync(MemoryScope.Default, "repo score", minTrust: 0.0);

        Assert.AreEqual(1, facts.Count);
        Assert.AreEqual(1, facts[0].OccurrenceCount);
    }

    [TestMethod]
    public async Task DuplicateObservationRaisesTrustAndCount()
    {
        var store = new InMemoryFactStore(TimeProvider.System);
        var observation = new Observation("Team treats rule Z as comment only", DateTimeOffset.UtcNow);
        await store.UpsertObservationAsync(MemoryScope.Default, observation);
        await store.UpsertObservationAsync(MemoryScope.Default, observation);

        var facts = await store.RecallAsync(MemoryScope.Default, "rule Z comment", minTrust: 0.0);

        Assert.AreEqual(2, facts[0].OccurrenceCount);
        Assert.IsTrue(facts[0].Trust > 0.5);
    }

    [TestMethod]
    public async Task RecallFiltersByMinTrust()
    {
        var store = new InMemoryFactStore(TimeProvider.System);
        await store.UpsertObservationAsync(MemoryScope.Default, new Observation("Single mention fact", DateTimeOffset.UtcNow));

        var facts = await store.RecallAsync(MemoryScope.Default, "single mention", minTrust: 0.6);

        Assert.AreEqual(0, facts.Count);
    }

    [TestMethod]
    public async Task RecallIsScopedByTemplate()
    {
        var store = new InMemoryFactStore(TimeProvider.System);
        await store.UpsertObservationAsync(new MemoryScope("A"), new Observation("alpha fact", DateTimeOffset.UtcNow));

        var facts = await store.RecallAsync(new MemoryScope("B"), "alpha", minTrust: 0.0);

        Assert.AreEqual(0, facts.Count);
    }
}

[TestClass]
public sealed class InMemoryVectorRecallTests
{
    [TestMethod]
    public async Task IndexThenRecallReturnsSimilarItem()
    {
        using var generator = new HashingEmbeddingGenerator();
        var store = new InMemoryVectorRecall();
        var embedding = (await generator.GenerateAsync(["react doctor score gating"])).Single().Vector;
        await store.IndexAsync(MemoryScope.Default, new MemoryRecord("1", "react doctor score gating", embedding));

        var query = (await generator.GenerateAsync(["react doctor score gating"])).Single().Vector;
        var hits = await store.RecallAsync(MemoryScope.Default, query, minScore: 0.5);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(RecallSource.Vector, hits[0].Source);
    }

    [TestMethod]
    public async Task RecallFiltersByMinScore()
    {
        using var generator = new HashingEmbeddingGenerator();
        var store = new InMemoryVectorRecall();
        var embedding = (await generator.GenerateAsync(["alpha"])).Single().Vector;
        await store.IndexAsync(MemoryScope.Default, new MemoryRecord("1", "alpha", embedding));

        var query = (await generator.GenerateAsync(["zeta omega unrelated"])).Single().Vector;
        var hits = await store.RecallAsync(MemoryScope.Default, query, minScore: 0.9);

        Assert.AreEqual(0, hits.Count);
    }
}
