namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Identifies which memory layer produced a <see cref="RecallItem"/>.
/// </summary>
public enum RecallSource
{
    /// <summary>
    /// The always-on workspace (L1).
    /// </summary>
    Workspace,

    /// <summary>
    /// The trust-scored fact store (L3).
    /// </summary>
    Fact,

    /// <summary>
    /// The semantic vector store (L5).
    /// </summary>
    Vector,
}
