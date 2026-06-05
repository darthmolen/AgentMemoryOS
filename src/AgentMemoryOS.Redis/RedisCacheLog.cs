using Microsoft.Extensions.Logging;

namespace AgentMemoryOS.Redis;

/// <summary>
/// High-performance logging messages emitted by the Redis cache-aside layer.
/// </summary>
internal static partial class RedisCacheLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Redis cache operation '{Operation}' failed; falling back to the inner store.")]
    public static partial void CacheFailure(ILogger logger, string operation, Exception exception);
}
