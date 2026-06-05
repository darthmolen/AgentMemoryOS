namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Provides access to the trust-scored fact store (L3) for a <see cref="MemoryScope"/>.
/// </summary>
public interface IFactStore
{
    /// <summary>
    /// Recalls facts relevant to a query whose trust meets or exceeds a threshold.
    /// </summary>
    /// <param name="scope">The partition to read.</param>
    /// <param name="query">The natural-language query to match facts against.</param>
    /// <param name="minTrust">The minimum trust a fact must have to be returned, in the range 0..1.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The matching facts, ordered by descending relevance.</returns>
    public Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges an observation into the fact store, raising trust on a near-duplicate or
    /// inserting a new fact otherwise.
    /// </summary>
    /// <param name="scope">The partition to write.</param>
    /// <param name="observation">The observation to merge.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous merge operation.</returns>
    public Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default);
}
