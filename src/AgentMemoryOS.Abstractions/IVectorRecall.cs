namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Provides semantic recall and indexing over the vector store (L5) for a
/// <see cref="MemoryScope"/>.
/// </summary>
public interface IVectorRecall
{
    /// <summary>
    /// Recalls items semantically similar to a query embedding above a score threshold.
    /// </summary>
    /// <param name="scope">The partition to read.</param>
    /// <param name="queryEmbedding">The embedding of the query to match against.</param>
    /// <param name="minScore">The minimum similarity a hit must have to be returned, in the range 0..1.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The matching items, ordered by descending similarity.</returns>
    public Task<IReadOnlyList<RecallItem>> RecallAsync(MemoryScope scope, ReadOnlyMemory<float> queryEmbedding, double minScore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a record so it can be recalled semantically.
    /// </summary>
    /// <param name="scope">The partition to write.</param>
    /// <param name="record">The record to index.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous index operation.</returns>
    public Task IndexAsync(MemoryScope scope, MemoryRecord record, CancellationToken cancellationToken = default);
}
