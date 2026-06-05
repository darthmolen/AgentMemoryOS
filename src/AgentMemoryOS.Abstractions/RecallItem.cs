namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Represents a single piece of recalled context returned from the memory tiers.
/// </summary>
/// <param name="Content">The recalled text.</param>
/// <param name="Score">The relevance score, in the range 0..1.</param>
/// <param name="Source">The memory layer that produced the item.</param>
public sealed record RecallItem(string Content, double Score, RecallSource Source);
