using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentMemoryOS.Redis;

/// <summary>
/// A time-to-live-only cache-aside decorator over an inner <see cref="IWorkspaceStore"/>. The
/// workspace is read-oriented in the single-agent baseline, so loads are cached with an
/// expiry and there is no write API to invalidate. A Redis outage fails open to the inner store.
/// </summary>
public sealed class CachingWorkspaceStore : IWorkspaceStore
{
    private const string Layer = "workspace";

    private readonly IWorkspaceStore inner;
    private readonly RedisCache cache;
    private readonly TimeSpan ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingWorkspaceStore"/> class.
    /// </summary>
    /// <param name="inner">The inner workspace store to read through.</param>
    /// <param name="connection">The Redis connection multiplexer backing the cache.</param>
    /// <param name="options">The cache options supplying the key prefix and workspace time-to-live.</param>
    /// <param name="logger">The logger used to record fail-open fallbacks.</param>
    public CachingWorkspaceStore(IWorkspaceStore inner, IConnectionMultiplexer connection, RedisCacheOptions options, ILogger<CachingWorkspaceStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);

        this.inner = inner;
        this.cache = new RedisCache(connection, logger, options.KeyPrefix);
        this.ttl = options.WorkspaceTtl;
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(MemoryScope scope, CancellationToken cancellationToken = default)
    {
        var scopeKey = RedisCache.ScopeKey(scope);
        var key = this.cache.EntryKey(Layer, scopeKey, "load");

        var (found, cached) = await this.cache.TryGetAsync<WorkspaceEnvelope>(key);
        if (found && cached is not null)
        {
            return cached.Text;
        }

        var result = await this.inner.LoadAsync(scope, cancellationToken);
        await this.cache.SetAsync(key, new WorkspaceEnvelope(result), this.ttl);
        return result;
    }

    /// <summary>
    /// Envelope around the nullable workspace text so a cached <see langword="null"/> result is
    /// distinguishable from a cache miss.
    /// </summary>
    /// <param name="Text">The cached workspace text, or <see langword="null"/> when none exists.</param>
    private sealed record WorkspaceEnvelope(string? Text);
}
