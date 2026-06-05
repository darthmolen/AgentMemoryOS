using AgentMemoryOS.Abstractions;
using Npgsql;

namespace AgentMemoryOS.Postgres;

/// <summary>
/// A PostgreSQL-backed <see cref="IFactStore"/> that materializes observations into
/// trust-scored facts via a count-based upsert merge, partitioned by <see cref="MemoryScope"/>.
/// Trust is derived from the occurrence count, never stored.
/// </summary>
public sealed class PostgresFactStore : IFactStore
{
    private readonly MemoryDataSource dataSource;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresFactStore"/> class.
    /// </summary>
    /// <param name="dataSource">The shared data source.</param>
    /// <param name="timeProvider">The time source used to stamp fact updates.</param>
    internal PostgresFactStore(MemoryDataSource dataSource, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.dataSource = dataSource;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var key = MemoryText.NormalizeKey(observation.Content);
        if (key.Length == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO maf_memory_facts (scope_key, content_key, content, count, updated_at)
            VALUES (@scope, @key, @content, 1, @now)
            ON CONFLICT (scope_key, content_key)
            DO UPDATE SET count = maf_memory_facts.count + 1, updated_at = @now;
            """;

        var now = this.timeProvider.GetUtcNow();

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scope", PostgresScope.Key(scope));
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("content", observation.Content);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryTerms = Terms(query);

        const string sql = """
            SELECT content, count, updated_at
            FROM maf_memory_facts
            WHERE scope_key = @scope;
            """;

        var matches = new List<(Fact Fact, int Relevance)>();

        await using (var connection = await this.dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("scope", PostgresScope.Key(scope));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var content = await reader.GetFieldValueAsync<string>(0, cancellationToken);
                var count = await reader.GetFieldValueAsync<int>(1, cancellationToken);
                var updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken);

                var relevance = Overlap(queryTerms, content);
                var trust = TrustModel.FromCount(count);
                if (relevance > 0 && trust >= minTrust)
                {
                    matches.Add((new Fact(content, trust, count, updatedAt), relevance));
                }
            }
        }

        return matches
            .OrderByDescending(candidate => candidate.Relevance)
            .ThenByDescending(candidate => candidate.Fact.Trust)
            .Select(candidate => candidate.Fact)
            .ToList();
    }

    private static int Overlap(HashSet<string> queryTerms, string content)
    {
        return Terms(content).Count(queryTerms.Contains);
    }

    private static HashSet<string> Terms(string text)
    {
        return MemoryText.NormalizeKey(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
