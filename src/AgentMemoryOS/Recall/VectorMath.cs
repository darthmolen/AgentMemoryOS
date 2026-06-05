namespace AgentMemoryOS.Recall;

internal static class VectorMath
{
    public static double CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0.0;
        }

        var first = a.Span;
        var second = b.Span;
        var dot = 0.0;
        var firstNorm = 0.0;
        var secondNorm = 0.0;
        for (var i = 0; i < first.Length; i++)
        {
            dot += first[i] * second[i];
            firstNorm += first[i] * first[i];
            secondNorm += second[i] * second[i];
        }

        if (firstNorm == 0.0 || secondNorm == 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(firstNorm) * Math.Sqrt(secondNorm));
    }
}
