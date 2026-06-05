using AgentMemoryOS;
using AgentMemoryOS.Postgres;
using Microsoft.Extensions.Configuration;

// Builder extensions live in the Microsoft.Extensions.DependencyInjection namespace so callers
// discover them through the standard using directive alongside AddTieredMemory.
#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Adds the PostgreSQL + pgvector backing store to a tiered-memory builder.
/// </summary>
public static class PostgresMemoryBuilderExtensions
{
    /// <summary>
    /// Uses PostgreSQL + pgvector as the durable fact and vector store.
    /// </summary>
    /// <param name="builder">The tiered-memory builder.</param>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <param name="embeddingDimensions">The width of the pgvector column; must match the embedder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ITieredMemoryBuilder UsePostgres(this ITieredMemoryBuilder builder, string connectionString, int embeddingDimensions = 256)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddTieredMemoryPostgres(connectionString, embeddingDimensions);
        return builder;
    }

    /// <summary>
    /// Uses PostgreSQL + pgvector, binding the connection string and embedding width from a
    /// configuration section (<see cref="PostgresMemoryOptions"/>).
    /// </summary>
    /// <param name="builder">The tiered-memory builder.</param>
    /// <param name="section">The configuration section to bind.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ITieredMemoryBuilder UsePostgres(this ITieredMemoryBuilder builder, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        var options = new PostgresMemoryOptions();
        section.Bind(options);
        return builder.UsePostgres(options.ConnectionString, options.EmbeddingDimensions);
    }
}
