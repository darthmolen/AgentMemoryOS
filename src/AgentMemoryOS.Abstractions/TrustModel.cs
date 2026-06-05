namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Derives a fact's trust from the number of supporting observations. Shared so every
/// backing store (in-memory, Postgres) scores facts identically.
/// </summary>
public static class TrustModel
{
    /// <summary>
    /// Derives a trust score in the range 0..1 from an occurrence count. Trust rises with
    /// agreement and saturates toward 1.0 (1 occurrence = 0.5, 2 = 0.67, 3 = 0.75, ...).
    /// </summary>
    /// <param name="occurrenceCount">The number of observations supporting the fact.</param>
    /// <returns>The derived trust score.</returns>
    public static double FromCount(int occurrenceCount)
    {
        var effective = occurrenceCount < 1 ? 1 : occurrenceCount;
        return 1.0 - (1.0 / (effective + 1));
    }
}
