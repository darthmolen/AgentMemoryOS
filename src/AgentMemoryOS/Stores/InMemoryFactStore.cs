using System.Collections.Concurrent;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Recall;

namespace AgentMemoryOS.Stores;

/// <summary>
/// An in-process <see cref="IFactStore"/> that materializes observations into trust-scored
/// facts via a count-based upsert merge, keyed by <see cref="MemoryScope"/>. This is the
/// zero-dependency default.
/// </summary>
public sealed class InMemoryFactStore : IFactStore
{
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<MemoryScope, ConcurrentDictionary<string, FactEntry>> partitions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryFactStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The time source used to stamp fact updates.</param>
    public InMemoryFactStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var key = MemoryGate.NormalizeKey(observation.Content);
        if (key.Length == 0)
        {
            return Task.CompletedTask;
        }

        var now = this.timeProvider.GetUtcNow();
        var partition = this.partitions.GetOrAdd(scope, _ => new ConcurrentDictionary<string, FactEntry>(StringComparer.Ordinal));
        partition.AddOrUpdate(
            key,
            _ => new FactEntry(observation.Content, 1, now),
            (_, existing) => existing with { Count = existing.Count + 1, UpdatedAt = now });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!this.partitions.TryGetValue(scope, out var partition))
        {
            return Task.FromResult<IReadOnlyList<Fact>>([]);
        }

        var queryTerms = Terms(query);
        var matches = partition.Values
            .Select(entry => new
            {
                Fact = new Fact(entry.Content, TrustModel.FromCount(entry.Count), entry.Count, entry.UpdatedAt),
                Relevance = Overlap(queryTerms, entry.Content),
            })
            .Where(candidate => candidate.Relevance > 0 && candidate.Fact.Trust >= minTrust)
            .OrderByDescending(candidate => candidate.Relevance)
            .ThenByDescending(candidate => candidate.Fact.Trust)
            .Select(candidate => candidate.Fact)
            .ToList();

        return Task.FromResult<IReadOnlyList<Fact>>(matches);
    }

    private static int Overlap(HashSet<string> queryTerms, string content)
    {
        return Terms(content).Count(queryTerms.Contains);
    }

    private static HashSet<string> Terms(string text)
    {
        return MemoryGate.NormalizeKey(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
