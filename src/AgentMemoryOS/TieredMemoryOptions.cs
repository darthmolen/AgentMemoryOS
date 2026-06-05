namespace AgentMemoryOS;

/// <summary>
/// Tunes recall gating and fact extraction for a <see cref="TieredMemoryProvider"/>.
/// </summary>
public sealed class TieredMemoryOptions
{
    /// <summary>
    /// The default instructions sent to the extractor model when capturing observations.
    /// </summary>
    public const string DefaultExtractionInstructions =
        "You extract durable, reusable facts from a conversation between a user and an assistant. " +
        "Return only stable, generally useful facts (preferences, conventions, decisions, corrections), " +
        "one per line, with no numbering or commentary. Ignore greetings, small talk, and one-off task " +
        "details. If there is nothing worth remembering, reply with the single word NONE.";

    /// <summary>
    /// Gets or sets the minimum trust a fact must have to be recalled, in the range 0..1.
    /// </summary>
    public double MinTrust { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the minimum similarity a vector hit must have to be recalled, in the range 0..1.
    /// </summary>
    public double MinVectorScore { get; set; } = 0.75;

    /// <summary>
    /// Gets or sets the maximum number of recalled items injected into a single invocation.
    /// </summary>
    public int MaxRecallItems { get; set; } = 8;

    /// <summary>
    /// Gets or sets the cosine threshold above which two recalled items are treated as duplicates.
    /// </summary>
    public double CosineDedupThreshold { get; set; } = 0.92;

    /// <summary>
    /// Gets or sets the instructions sent to the extractor model when capturing observations.
    /// </summary>
    public string ExtractionInstructions { get; set; } = DefaultExtractionInstructions;
}
