using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AgentMemoryOS.Redis;

/// <summary>
/// Dependency-injection extensions that wrap the already-registered memory stores with their
/// Redis cache-aside decorators.
/// </summary>
public static class RedisMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis cache-aside layer, decorating whatever <see cref="IFactStore"/>,
    /// <see cref="IVectorRecall"/>, and <see cref="IWorkspaceStore"/> are already registered.
    /// Not calling this method leaves the inner stores untouched, so Redis is disabled by
    /// construction.
    /// </summary>
    /// <param name="services">The service collection to register the decorators with.</param>
    /// <param name="connectionString">The StackExchange.Redis connection string.</param>
    /// <param name="options">The cache options, or <see langword="null"/> to use the defaults.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddTieredMemoryRedisCache(this IServiceCollection services, string connectionString, RedisCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var resolved = options ?? new RedisCacheOptions();
        services.TryAddSingleton(resolved);
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));

        Decorate<IFactStore>(services, (inner, provider) => new CachingFactStore(
            inner,
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<RedisCacheOptions>(),
            provider.GetRequiredService<ILogger<CachingFactStore>>()));

        Decorate<IVectorRecall>(services, (inner, provider) => new CachingVectorRecall(
            inner,
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<RedisCacheOptions>(),
            provider.GetRequiredService<ILogger<CachingVectorRecall>>()));

        Decorate<IWorkspaceStore>(services, (inner, provider) => new CachingWorkspaceStore(
            inner,
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<RedisCacheOptions>(),
            provider.GetRequiredService<ILogger<CachingWorkspaceStore>>()));

        return services;
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> decorate)
        where TService : class
    {
        for (var index = 0; index < services.Count; index++)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(TService))
            {
                continue;
            }

            var inner = descriptor;
            services[index] = ServiceDescriptor.Describe(
                typeof(TService),
                provider => decorate((TService)CreateInner(inner, provider), provider),
                descriptor.Lifetime);
        }
    }

    private static object CreateInner(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(provider);
        }

        return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
