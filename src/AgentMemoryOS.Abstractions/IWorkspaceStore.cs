namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Provides read access to the always-on workspace (L1) for a <see cref="MemoryScope"/>.
/// </summary>
/// <remarks>
/// The workspace is read-oriented in the single-agent baseline; it is seeded out of band
/// and surfaced verbatim on every recall.
/// </remarks>
public interface IWorkspaceStore
{
    /// <summary>
    /// Loads the workspace text for the given scope.
    /// </summary>
    /// <param name="scope">The partition to read.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The workspace text, or <see langword="null"/> when none exists for the scope.</returns>
    public Task<string?> LoadAsync(MemoryScope scope, CancellationToken cancellationToken = default);
}
