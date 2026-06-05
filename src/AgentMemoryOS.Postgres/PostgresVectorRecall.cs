using AgentMemoryOS.Abstractions;
using Npgsql;
using Pgvector;

namespace AgentMemoryOS.Postgres;

/// <summary>
/// A PostgreSQL + pgvector-backed <see cref="IVectorRecall"/> that indexes records per
/// <see cref="MemoryScope"/> and recalls them by cosine similarity.
/// </summary>
public sealed class PostgresVectorRecall : IVectorRecall
{
    private readonly MemoryDataSource dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVectorRecall"/> class.
    /// </summary>
    /// <param name="dataSource">The shared data source.</param>
    internal PostgresVectorRecall(MemoryDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        this.dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task IndexAsync(MemoryScope scope, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        const string sql = """
            INSERT INTO maf_memory_vectors (scope_key, id, content, embedding)
            VALUES (@scope, @id, @content, @embedding)
            ON CONFLICT (scope_key, id)
            DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding;
            """;

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scope", PostgresScope.Key(scope));
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("content", record.Content);
        command.Parameters.AddWithValue("embedding", new Vector(record.Embedding));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecallItem>> RecallAsync(MemoryScope scope, ReadOnlyMemory<float> queryEmbedding, double minScore, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT content, 1 - (embedding <=> @query) AS score
            FROM maf_memory_vectors
            WHERE scope_key = @scope
            ORDER BY score DESC;
            """;

        var hits = new List<RecallItem>();

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scope", PostgresScope.Key(scope));
        command.Parameters.AddWithValue("query", new Vector(queryEmbedding));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var content = await reader.GetFieldValueAsync<string>(0, cancellationToken);
            var score = await reader.GetFieldValueAsync<double>(1, cancellationToken);
            if (score >= minScore)
            {
                hits.Add(new RecallItem(content, score, RecallSource.Vector));
            }
        }

        return hits;
    }
}
