namespace AgentMemoryOS.Redis;

/// <summary>
/// Configures the Redis cache-aside layer: the key prefix that namespaces every entry and
/// the time-to-live applied to each cached memory layer.
/// </summary>
public sealed class RedisCacheOptions
{
    /// <summary>
    /// Gets or sets the prefix prepended to every Redis key so that cache entries can be
    /// isolated from other tenants of the same Redis instance. Defaults to <c>maf:memoryos</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "maf:memoryos";

    /// <summary>
    /// Gets or sets the time-to-live for cached fact-store recalls. Defaults to five minutes.
    /// </summary>
    public TimeSpan FactTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the time-to-live for cached vector recalls. Defaults to five minutes.
    /// </summary>
    public TimeSpan VectorTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the time-to-live for cached workspace loads. Defaults to ten minutes.
    /// </summary>
    public TimeSpan WorkspaceTtl { get; set; } = TimeSpan.FromMinutes(10);
}
