namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Represents an immutable unit of information extracted from a conversation, before it is
/// merged into a trust-scored <see cref="Fact"/> by the reconciler.
/// </summary>
/// <param name="Content">The natural-language statement that was observed.</param>
/// <param name="CreatedAt">The time the observation was captured.</param>
public sealed record Observation(string Content, DateTimeOffset CreatedAt);
