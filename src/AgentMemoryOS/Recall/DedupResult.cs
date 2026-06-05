namespace AgentMemoryOS.Recall;

internal sealed record DedupResult(IReadOnlyList<ScoredCandidate> Kept, IReadOnlySet<string> KeptKeys);
