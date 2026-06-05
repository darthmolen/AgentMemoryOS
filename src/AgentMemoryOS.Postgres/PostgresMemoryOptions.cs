namespace AgentMemoryOS.Postgres;

/// <summary>
/// Configures the PostgreSQL + pgvector backing store.
/// </summary>
public sealed class PostgresMemoryOptions
{
    /// <summary>
    /// Gets or sets the Npgsql connection string used to reach the database.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dimensionality of the embedding vectors stored in the vector table.
    /// This must match the embedder in use and the <c>vector(dim)</c> column width.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 256;
}
