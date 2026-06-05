namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Represents an item stored in the semantic vector index (L5).
/// </summary>
/// <param name="Id">The stable identifier of the record.</param>
/// <param name="Content">The natural-language content.</param>
/// <param name="Embedding">The embedding vector for the content.</param>
public sealed record MemoryRecord(string Id, string Content, ReadOnlyMemory<float> Embedding);
