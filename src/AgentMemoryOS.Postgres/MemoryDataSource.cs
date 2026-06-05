using System.Globalization;
using System.Text;
using Npgsql;

namespace AgentMemoryOS.Postgres;

/// <summary>
/// Owns the shared <see cref="NpgsqlDataSource"/> and applies the idempotent schema bootstrap
/// exactly once on first use.
/// </summary>
internal sealed class MemoryDataSource : IAsyncDisposable
{
    private const string MigrationResource = "AgentMemoryOS.Postgres.migrations.001_init.sql";

    private readonly NpgsqlDataSource dataSource;
    private readonly SemaphoreSlim bootstrapGate = new(1, 1);
    private volatile bool bootstrapped;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDataSource"/> class.
    /// </summary>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <param name="embeddingDimensions">The width of the pgvector column.</param>
    public MemoryDataSource(string connectionString, int embeddingDimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(embeddingDimensions, 1);

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        this.dataSource = builder.Build();
        this.EmbeddingDimensions = embeddingDimensions;
    }

    /// <summary>
    /// Gets the configured embedding dimensionality.
    /// </summary>
    public int EmbeddingDimensions { get; }

    /// <summary>
    /// Opens a connection, ensuring the schema has been bootstrapped first.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await this.EnsureBootstrappedAsync(cancellationToken);
        return await this.dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.bootstrapGate.Dispose();
        await this.dataSource.DisposeAsync();
    }

    private static string LoadMigration()
    {
        var assembly = typeof(MemoryDataSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(MigrationResource)
            ?? throw new InvalidOperationException($"Embedded migration '{MigrationResource}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private async Task EnsureBootstrappedAsync(CancellationToken cancellationToken)
    {
        if (this.bootstrapped)
        {
            return;
        }

        await this.bootstrapGate.WaitAsync(cancellationToken);
        try
        {
            if (this.bootstrapped)
            {
                return;
            }

            var sql = LoadMigration().Replace(
                "{dim}",
                this.EmbeddingDimensions.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

            await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken);
#pragma warning disable CA2100 // The SQL is a trusted embedded migration with the dimension substituted from validated configuration.
            await using (var command = new NpgsqlCommand(sql, connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
#pragma warning restore CA2100

            await connection.ReloadTypesAsync(cancellationToken);

            this.bootstrapped = true;
        }
        finally
        {
            this.bootstrapGate.Release();
        }
    }
}
