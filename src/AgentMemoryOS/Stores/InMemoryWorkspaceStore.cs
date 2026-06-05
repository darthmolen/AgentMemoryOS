using System.Collections.Concurrent;
using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Stores;

/// <summary>
/// An in-process <see cref="IWorkspaceStore"/> that keeps the always-on workspace (L1) for
/// each scope in memory. This is the zero-dependency default.
/// </summary>
public sealed class InMemoryWorkspaceStore : IWorkspaceStore
{
    private readonly ConcurrentDictionary<MemoryScope, string> entries = new();

    /// <summary>
    /// Seeds or replaces the workspace text for a scope.
    /// </summary>
    /// <param name="scope">The partition to write.</param>
    /// <param name="text">The workspace text to store.</param>
    public void Set(MemoryScope scope, string text)
    {
        this.entries[scope] = text;
    }

    /// <inheritdoc />
    public Task<string?> LoadAsync(MemoryScope scope, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.entries.TryGetValue(scope, out var text) ? text : null);
    }
}
