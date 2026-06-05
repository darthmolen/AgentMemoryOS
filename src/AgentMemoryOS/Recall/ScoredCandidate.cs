using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Recall;

internal sealed record ScoredCandidate(string Content, double Score, RecallSource Source, ReadOnlyMemory<float>? Embedding);
