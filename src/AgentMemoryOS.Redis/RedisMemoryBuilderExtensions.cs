using AgentMemoryOS;
using AgentMemoryOS.Redis;

// Builder extensions live in the Microsoft.Extensions.DependencyInjection namespace so callers
// discover them through the standard using directive alongside AddTieredMemory.
#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Adds the Redis cache-aside layer to a tiered-memory builder.
/// </summary>
public static class RedisMemoryBuilderExtensions
{
    /// <summary>
    /// Wraps the configured stores with Redis cache-aside decorators. The decoration is deferred
    /// until after the stores are registered, so it works regardless of call order.
    /// </summary>
    /// <param name="builder">The tiered-memory builder.</param>
    /// <param name="connectionString">The StackExchange.Redis connection string.</param>
    /// <param name="options">The cache options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ITieredMemoryBuilder UseRedisCache(this ITieredMemoryBuilder builder, string connectionString, RedisCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPostConfiguration(services => services.AddTieredMemoryRedisCache(connectionString, options));
        return builder;
    }
}
