using AgentMemoryOS.Abstractions;

namespace AgentMemoryOS.Recall;

internal static class MemoryGate
{
    private static readonly HashSet<string> SocialClosers = new(StringComparer.Ordinal)
    {
        "thanks", "thank you", "thx", "ty", "ok", "okay", "k", "got it", "great", "cool",
        "nice", "sure", "yes", "no", "yep", "nope", "np", "no problem", "youre welcome",
        "welcome", "done", "perfect", "awesome", "bye", "goodbye", "cheers", "lol",
    };

    public static bool IsTrivial(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var key = NormalizeKey(text);
        return key.Length == 0 || SocialClosers.Contains(key);
    }

    public static string NormalizeKey(string text)
    {
        return MemoryText.NormalizeKey(text);
    }

    public static IReadOnlyList<ScoredCandidate> Gate(IEnumerable<ScoredCandidate> candidates, double minScore, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate.Score >= minScore)
            .OrderByDescending(candidate => candidate.Score)
            .Take(maxItems)
            .ToList();
    }

    public static DedupResult Dedup(IEnumerable<ScoredCandidate> candidates, IReadOnlySet<string> alreadySeen, double cosineThreshold)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadySeen);

        var kept = new List<ScoredCandidate>();
        var keptKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score))
        {
            var key = NormalizeKey(candidate.Content);
            if (key.Length == 0 || alreadySeen.Contains(key) || keptKeys.Contains(key))
            {
                continue;
            }

            if (IsSemanticDuplicate(candidate, kept, cosineThreshold))
            {
                continue;
            }

            kept.Add(candidate);
            keptKeys.Add(key);
        }

        return new DedupResult(kept, keptKeys);
    }

    private static bool IsSemanticDuplicate(ScoredCandidate candidate, List<ScoredCandidate> kept, double cosineThreshold)
    {
        if (candidate.Embedding is not { } embedding)
        {
            return false;
        }

        foreach (var existing in kept)
        {
            if (existing.Embedding is { } other && VectorMath.CosineSimilarity(embedding, other) >= cosineThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
