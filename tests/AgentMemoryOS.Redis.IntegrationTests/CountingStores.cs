namespace AgentMemoryOS.Redis.IntegrationTests;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentMemoryOS.Abstractions;

/// <summary>
/// A fake fact store that records how many times each method was called so tests can assert
/// whether a recall was served from the cache or fell through to the inner store.
/// </summary>
public sealed class CountingFactStore : IFactStore
{
    public int RecallCount { get; private set; }

    public int UpsertCount { get; private set; }

    public IReadOnlyList<Fact> Result { get; set; } =
        [new Fact("repo gates on score", 0.8, 3, DateTimeOffset.UnixEpoch)];

    public Task<IReadOnlyList<Fact>> RecallAsync(MemoryScope scope, string query, double minTrust, CancellationToken cancellationToken = default)
    {
        this.RecallCount++;
        return Task.FromResult(this.Result);
    }

    public Task UpsertObservationAsync(MemoryScope scope, Observation observation, CancellationToken cancellationToken = default)
    {
        this.UpsertCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A fake vector recall that records call counts.
/// </summary>
public sealed class CountingVectorRecall : IVectorRecall
{
    public int RecallCount { get; private set; }

    public int IndexCount { get; private set; }

    public IReadOnlyList<RecallItem> Result { get; set; } =
        [new RecallItem("semantic hit", 0.9, RecallSource.Vector)];

    public Task<IReadOnlyList<RecallItem>> RecallAsync(MemoryScope scope, ReadOnlyMemory<float> queryEmbedding, double minScore, CancellationToken cancellationToken = default)
    {
        this.RecallCount++;
        return Task.FromResult(this.Result);
    }

    public Task IndexAsync(MemoryScope scope, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        this.IndexCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A fake workspace store that records load counts.
/// </summary>
public sealed class CountingWorkspaceStore : IWorkspaceStore
{
    public int LoadCount { get; private set; }

    public string? Result { get; set; } = "# Workspace";

    public Task<string?> LoadAsync(MemoryScope scope, CancellationToken cancellationToken = default)
    {
        this.LoadCount++;
        return Task.FromResult(this.Result);
    }
}
