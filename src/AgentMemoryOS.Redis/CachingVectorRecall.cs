using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentMemoryOS.Redis;

/// <summary>
/// A cache-aside decorator over an inner <see cref="IVectorRecall"/>. Recalls are keyed by a
/// stable hash of the query embedding and served from Redis on a hit; indexing invalidates the
/// affected scope's cached recalls. A Redis outage fails open to the inner store.
/// </summary>
public sealed class CachingVectorRecall : IVectorRecall
{
    private const string Layer = "vector";

    private readonly IVectorRecall inner;
    private readonly RedisCache cache;
    private readonly TimeSpan ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingVectorRecall"/> class.
    /// </summary>
    /// <param name="inner">The inner vector recall to read through and write through.</param>
    /// <param name="connection">The Redis connection multiplexer backing the cache.</param>
    /// <param name="options">The cache options supplying the key prefix and vector time-to-live.</param>
    /// <param name="logger">The logger used to record fail-open fallbacks.</param>
    public CachingVectorRecall(IVectorRecall inner, IConnectionMultiplexer connection, RedisCacheOptions options, ILogger<CachingVectorRecall> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);

        this.inner = inner;
        this.cache = new RedisCache(connection, logger, options.KeyPrefix);
        this.ttl = options.VectorTtl;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecallItem>> RecallAsync(MemoryScope scope, ReadOnlyMemory<float> queryEmbedding, double minScore, CancellationToken cancellationToken = default)
    {
        var scopeKey = RedisCache.ScopeKey(scope);
        var discriminator = Discriminator(queryEmbedding.Span, minScore);
        var key = this.cache.EntryKey(Layer, scopeKey, discriminator);

        var (found, cached) = await this.cache.TryGetAsync<List<RecallItem>>(key);
        if (found && cached is not null)
        {
            return cached;
        }

        var result = await this.inner.RecallAsync(scope, queryEmbedding, minScore, cancellationToken);
        await this.cache.SetTaggedAsync(Layer, scopeKey, key, result, this.ttl);
        return result;
    }

    /// <inheritdoc />
    public async Task IndexAsync(MemoryScope scope, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        await this.inner.IndexAsync(scope, record, cancellationToken);
        await this.cache.InvalidateScopeAsync(Layer, RedisCache.ScopeKey(scope));
    }

    private static string Discriminator(ReadOnlySpan<float> embedding, double minScore)
    {
        var buffer = new byte[(embedding.Length * sizeof(float)) + sizeof(double)];
        var floats = MemoryMarshal.AsBytes(embedding);
        floats.CopyTo(buffer);
        BitConverter.TryWriteBytes(buffer.AsSpan(embedding.Length * sizeof(float)), minScore);

        var hash = SHA256.HashData(buffer);
        return Convert.ToHexString(hash);
    }
}
