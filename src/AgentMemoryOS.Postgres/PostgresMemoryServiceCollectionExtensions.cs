using AgentMemoryOS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentMemoryOS.Postgres;

/// <summary>
/// Registers the PostgreSQL + pgvector backing store with a dependency-injection container.
/// </summary>
public static class PostgresMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the PostgreSQL-backed <see cref="IFactStore"/> and <see cref="IVectorRecall"/> to the
    /// service collection, along with the shared data source and options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <param name="embeddingDimensions">The width of the pgvector column; must match the embedder.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTieredMemoryPostgres(this IServiceCollection services, string connectionString, int embeddingDimensions = 256)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(embeddingDimensions, 1);

        services.TryAddSingleton(TimeProvider.System);

        var options = new PostgresMemoryOptions
        {
            ConnectionString = connectionString,
            EmbeddingDimensions = embeddingDimensions,
        };
        services.TryAddSingleton(options);

        services.TryAddSingleton(_ => new MemoryDataSource(connectionString, embeddingDimensions));

        services.TryAddSingleton<IFactStore>(provider =>
            new PostgresFactStore(
                provider.GetRequiredService<MemoryDataSource>(),
                provider.GetRequiredService<TimeProvider>()));

        services.TryAddSingleton<IVectorRecall>(provider =>
            new PostgresVectorRecall(provider.GetRequiredService<MemoryDataSource>()));

        return services;
    }
}
