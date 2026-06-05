namespace AgentMemoryOS.Redis.IntegrationTests;

using System;
using System.Threading.Tasks;
using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

[TestClass]
public sealed class RedisCachingTests
{
    private static readonly MemoryScope Scope = new("template", "instance");

    private RedisContainer container = null!;
    private ConnectionMultiplexer connection = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        this.container = new RedisBuilder("redis:7-alpine").Build();
        await this.container.StartAsync();
        this.connection = await ConnectionMultiplexer.ConnectAsync(this.container.GetConnectionString());
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (this.connection is not null)
        {
            await this.connection.DisposeAsync();
        }

        if (this.container is not null)
        {
            await this.container.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task FactRecallMissThenHitServesSecondFromCache()
    {
        var inner = new CountingFactStore();
        var store = new CachingFactStore(inner, this.connection, new RedisCacheOptions(), NullLogger<CachingFactStore>.Instance);

        var first = await store.RecallAsync(Scope, "what gates the repo", 0.5);
        var second = await store.RecallAsync(Scope, "what gates the repo", 0.5);

        Assert.AreEqual(1, inner.RecallCount, "second identical recall should be served from cache");
        Assert.AreEqual(first.Count, second.Count);
        Assert.AreEqual(first[0].Content, second[0].Content);
    }

    [TestMethod]
    public async Task FactUpsertInvalidatesScopeCache()
    {
        var inner = new CountingFactStore();
        var store = new CachingFactStore(inner, this.connection, new RedisCacheOptions(), NullLogger<CachingFactStore>.Instance);

        await store.RecallAsync(Scope, "query", 0.5);
        Assert.AreEqual(1, inner.RecallCount);

        await store.UpsertObservationAsync(Scope, new Observation("new fact", DateTimeOffset.UtcNow));

        await store.RecallAsync(Scope, "query", 0.5);
        Assert.AreEqual(2, inner.RecallCount, "recall after a write must miss the invalidated cache");
    }

    [TestMethod]
    public async Task VectorRecallMissThenHitServesSecondFromCache()
    {
        var inner = new CountingVectorRecall();
        var store = new CachingVectorRecall(inner, this.connection, new RedisCacheOptions(), NullLogger<CachingVectorRecall>.Instance);
        var embedding = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]);

        await store.RecallAsync(Scope, embedding, 0.5);
        await store.RecallAsync(Scope, embedding, 0.5);

        Assert.AreEqual(1, inner.RecallCount, "second identical vector recall should be served from cache");
    }

    [TestMethod]
    public async Task VectorIndexInvalidatesScopeCache()
    {
        var inner = new CountingVectorRecall();
        var store = new CachingVectorRecall(inner, this.connection, new RedisCacheOptions(), NullLogger<CachingVectorRecall>.Instance);
        var embedding = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]);

        await store.RecallAsync(Scope, embedding, 0.5);
        await store.IndexAsync(Scope, new MemoryRecord("id", "content", embedding));
        await store.RecallAsync(Scope, embedding, 0.5);

        Assert.AreEqual(2, inner.RecallCount, "recall after indexing must miss the invalidated cache");
    }

    [TestMethod]
    public async Task WorkspaceTtlOnlyCachingHitThenExpiry()
    {
        var inner = new CountingWorkspaceStore();
        var options = new RedisCacheOptions { WorkspaceTtl = TimeSpan.FromSeconds(2) };
        var store = new CachingWorkspaceStore(inner, this.connection, options, NullLogger<CachingWorkspaceStore>.Instance);

        var first = await store.LoadAsync(Scope);
        var second = await store.LoadAsync(Scope);

        Assert.AreEqual(1, inner.LoadCount, "second load within the TTL should be served from cache");
        Assert.AreEqual(first, second);

        var scopeKey = $"{Scope.TemplateId}/{Scope.InstanceId}";
        var workspaceKey = $"{options.KeyPrefix}:workspace:{scopeKey}:load";
        await this.WaitForKeyExpiryAsync(workspaceKey);

        await store.LoadAsync(Scope);
        Assert.AreEqual(2, inner.LoadCount, "load after the TTL expires must fall through to the inner store");
    }

    [TestMethod]
    public async Task FactRecallFailsOpenWhenRedisUnreachable()
    {
        var inner = new CountingFactStore();
        await using var broken = await CreateUnreachableConnectionAsync();
        var store = new CachingFactStore(inner, broken, new RedisCacheOptions(), NullLogger<CachingFactStore>.Instance);

        var result = await store.RecallAsync(Scope, "query", 0.5);
        var again = await store.RecallAsync(Scope, "query", 0.5);

        Assert.AreEqual(1, result.Count, "inner results must still be returned when Redis is unreachable");
        Assert.AreEqual(2, inner.RecallCount, "every recall falls through to the inner store while Redis is down");
        Assert.AreEqual(result.Count, again.Count);
    }

    private static async Task<ConnectionMultiplexer> CreateUnreachableConnectionAsync()
    {
        var configuration = new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6390 } },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        };

        return await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    private async Task WaitForKeyExpiryAsync(string key)
    {
        var database = this.connection.GetDatabase();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await database.KeyExistsAsync(key))
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Key '{key}' did not expire within the timeout.");
    }
}
