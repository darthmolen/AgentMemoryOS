namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Represents a trust-scored statement materialized from one or more
/// <see cref="Observation"/> instances by the reconciler.
/// </summary>
/// <param name="Content">The natural-language statement.</param>
/// <param name="Trust">The derived trust score, in the range 0..1.</param>
/// <param name="OccurrenceCount">The number of observations that support the fact.</param>
/// <param name="UpdatedAt">The time the fact was last reinforced.</param>
public sealed record Fact(string Content, double Trust, int OccurrenceCount, DateTimeOffset UpdatedAt);
