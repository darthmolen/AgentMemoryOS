using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentMemoryOS.Redis;

/// <summary>
/// A cache-aside decorator over an inner <see cref="IFactStore"/>. Recalls are served from
/// Redis on a hit and populated on a miss with a time-to-live; upserts invalidate the
/// affected scope's cached recalls. A Redis outage fails open to the inner store.
/// </summary>
public sealed class CachingFactStore : IFactStore
{
    private const string Layer = "fact";

    private readonly IFactStore inner;
    private readonly RedisCache cache;
    private readonly TimeSpan ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingFactStore"/> class.
    /// </summary>
    /// <param name="inner">The inner fact store to read through and write through.</param>
    /// <param name="connection">The Redis connection multiplexer backing the cache.</param>
    /// <param name="options">The cache options supplying the key prefix and fact time-to-live.</param>
    /// <param name="logger">The logger used to record fail-open fallbacks.</param>
    public CachingFactStore(IFactStore inner, IConnectionMultiplexer connection, RedisCacheOptions options, ILogger<CachingFactStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);

        this.inner = inner;
        this.cache = new RedisCache(connection, logger, options.KeyPrefix);
        this.ttl = options.FactTtl;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scopeKey = RedisCache.ScopeKey(scope);
        var discriminator = Discriminator(query, minTrust);
        var key = this.cache.EntryKey(Layer, scopeKey, discriminator);

        var (found, cached) = await this.cache.TryGetAsync<List<Fact>>(key);
        if (found && cached is not null)
        {
            return cached;
        }

        var result = await this.inner.RecallAsync(scope, query, minTrust, cancellationToken);
        await this.cache.SetTaggedAsync(Layer, scopeKey, key, result, this.ttl);
        return result;
    }

    /// <inheritdoc />
    public async Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default)
    {
        await this.inner.UpsertObservationAsync(scope, observation, cancellationToken);
        await this.cache.InvalidateScopeAsync(Layer, RedisCache.ScopeKey(scope));
    }

    private static string Discriminator(string query, double minTrust)
    {
        var normalized = MemoryText.NormalizeKey(query);
        var trust = minTrust.ToString("R", CultureInfo.InvariantCulture);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalized}|{trust}"));
        return Convert.ToHexString(bytes);
    }
}
