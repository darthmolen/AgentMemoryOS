namespace AgentMemoryOS.Postgres.IntegrationTests;

using System;
using System.Threading.Tasks;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Postgres;
using Testcontainers.PostgreSql;

[TestClass]
public sealed class PostgresMemoryTests
{
    private const int Dimensions = 8;

    private static PostgreSqlContainer container = null!;
    private static MemoryDataSource dataSource = null!;
    private static PostgresFactStore factStore = null!;
    private static PostgresVectorRecall vectorRecall = null!;

    private static PostgresFactStore FactStore => factStore;

    private static PostgresVectorRecall VectorRecall => vectorRecall;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext context)
    {
        _ = context;

        container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .Build();
        await container.StartAsync();

        dataSource = new MemoryDataSource(container.GetConnectionString(), Dimensions);
        factStore = new PostgresFactStore(dataSource, TimeProvider.System);
        vectorRecall = new PostgresVectorRecall(dataSource);
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task UpsertThenRecallReturnsFact()
    {
        var scope = NewScope();
        await FactStore.UpsertObservationAsync(scope, new Observation("Repo gates on score 50", DateTimeOffset.UtcNow));

        var facts = await FactStore.RecallAsync(scope, "repo score", minTrust: 0.0);

        Assert.AreEqual(1, facts.Count);
        Assert.AreEqual(1, facts[0].OccurrenceCount);
        Assert.AreEqual("Repo gates on score 50", facts[0].Content);
    }

    [TestMethod]
    public async Task DuplicateObservationRaisesTrustAndCount()
    {
        var scope = NewScope();
        var observation = new Observation("Team treats rule Z as comment only", DateTimeOffset.UtcNow);
        await FactStore.UpsertObservationAsync(scope, observation);
        await FactStore.UpsertObservationAsync(scope, observation);

        var facts = await FactStore.RecallAsync(scope, "rule Z comment", minTrust: 0.0);

        Assert.AreEqual(1, facts.Count);
        Assert.AreEqual(2, facts[0].OccurrenceCount);
        Assert.IsTrue(facts[0].Trust > 0.5);
    }

    [TestMethod]
    public async Task RecallFiltersByMinTrust()
    {
        var scope = NewScope();
        await FactStore.UpsertObservationAsync(scope, new Observation("Single mention fact", DateTimeOffset.UtcNow));

        var facts = await FactStore.RecallAsync(scope, "single mention", minTrust: 0.6);

        Assert.AreEqual(0, facts.Count);
    }

    [TestMethod]
    public async Task RecallOnlyReturnsTermOverlapMatches()
    {
        var scope = NewScope();
        await FactStore.UpsertObservationAsync(scope, new Observation("alpha beta gamma", DateTimeOffset.UtcNow));
        await FactStore.UpsertObservationAsync(scope, new Observation("delta epsilon zeta", DateTimeOffset.UtcNow));

        var facts = await FactStore.RecallAsync(scope, "alpha", minTrust: 0.0);

        Assert.AreEqual(1, facts.Count);
        Assert.AreEqual("alpha beta gamma", facts[0].Content);
    }

    [TestMethod]
    public async Task RecallIsScopedByTemplate()
    {
        await FactStore.UpsertObservationAsync(new MemoryScope("A"), new Observation("alpha fact", DateTimeOffset.UtcNow));

        var facts = await FactStore.RecallAsync(new MemoryScope("B"), "alpha", minTrust: 0.0);

        Assert.AreEqual(0, facts.Count);
    }

    [TestMethod]
    public async Task IndexThenRecallReturnsSimilarItem()
    {
        var scope = NewScope();
        var indexed = UnitVector(0);
        await VectorRecall.IndexAsync(scope, new MemoryRecord("1", "react doctor score gating", indexed));

        var hits = await VectorRecall.RecallAsync(scope, UnitVector(0), minScore: 0.0);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("react doctor score gating", hits[0].Content);
        Assert.AreEqual(RecallSource.Vector, hits[0].Source);
        Assert.IsTrue(hits[0].Score > 0.99);
    }

    [TestMethod]
    public async Task VectorRecallFiltersByMinScore()
    {
        var scope = NewScope();
        await VectorRecall.IndexAsync(scope, new MemoryRecord("1", "orthogonal", UnitVector(0)));

        var hits = await VectorRecall.RecallAsync(scope, UnitVector(1), minScore: 0.5);

        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public async Task VectorIndexUpsertsById()
    {
        var scope = NewScope();
        await VectorRecall.IndexAsync(scope, new MemoryRecord("same", "first", UnitVector(0)));
        await VectorRecall.IndexAsync(scope, new MemoryRecord("same", "second", UnitVector(0)));

        var hits = await VectorRecall.RecallAsync(scope, UnitVector(0), minScore: 0.0);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("second", hits[0].Content);
    }

    [TestMethod]
    public async Task VectorRecallIsScopedByTemplate()
    {
        await VectorRecall.IndexAsync(new MemoryScope("VA"), new MemoryRecord("1", "scoped", UnitVector(0)));

        var hits = await VectorRecall.RecallAsync(new MemoryScope("VB"), UnitVector(0), minScore: 0.0);

        Assert.AreEqual(0, hits.Count);
    }

    private static MemoryScope NewScope()
    {
        return new MemoryScope("t-" + Guid.NewGuid().ToString("N"));
    }

    private static ReadOnlyMemory<float> UnitVector(int axis)
    {
        var values = new float[Dimensions];
        values[axis] = 1f;
        return values;
    }
}
