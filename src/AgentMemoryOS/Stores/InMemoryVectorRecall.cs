using System.Collections.Concurrent;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Recall;

namespace AgentMemoryOS.Stores;

/// <summary>
/// An in-process <see cref="IVectorRecall"/> that indexes records per <see cref="MemoryScope"/>
/// and recalls them by cosine similarity. This is the zero-dependency default.
/// </summary>
public sealed class InMemoryVectorRecall : IVectorRecall
{
    private readonly ConcurrentDictionary<MemoryScope, ConcurrentDictionary<string, MemoryRecord>> partitions = new();

    /// <inheritdoc />
    public Task IndexAsync(MemoryScope scope, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var partition = this.partitions.GetOrAdd(scope, _ => new ConcurrentDictionary<string, MemoryRecord>(StringComparer.Ordinal));
        partition[record.Id] = record;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecallItem>> RecallAsync(MemoryScope scope, ReadOnlyMemory<float> queryEmbedding, double minScore, CancellationToken cancellationToken = default)
    {
        if (!this.partitions.TryGetValue(scope, out var partition))
        {
            return Task.FromResult<IReadOnlyList<RecallItem>>([]);
        }

        var hits = partition.Values
            .Select(record => new RecallItem(record.Content, VectorMath.CosineSimilarity(queryEmbedding, record.Embedding), RecallSource.Vector))
            .Where(item => item.Score >= minScore)
            .OrderByDescending(item => item.Score)
            .ToList();

        return Task.FromResult<IReadOnlyList<RecallItem>>(hits);
    }
}
