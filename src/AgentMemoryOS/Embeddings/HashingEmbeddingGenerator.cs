using Microsoft.Extensions.AI;

namespace AgentMemoryOS.Embeddings;

/// <summary>
/// A deterministic, dependency-free embedding generator that maps text into a fixed-size
/// vector using feature hashing. It needs no model download or GPU, which keeps the
/// in-memory default and unit tests fully offline and reproducible.
/// </summary>
/// <remarks>
/// The vectors are lexical, not semantic: identical text yields identical vectors and
/// shared vocabulary yields similarity, but unrelated wording yields near-orthogonal
/// vectors. Swap in a real model-backed <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
/// (for example a local ONNX model or a vLLM embeddings endpoint) for semantic recall.
/// </remarks>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int DimensionCount = 256;

    /// <summary>
    /// Gets the dimensionality of the embeddings this generator produces.
    /// </summary>
    public static int Dimensions => DimensionCount;

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            embeddings.Add(new Embedding<float>(Embed(value ?? string.Empty)));
        }

        return Task.FromResult(embeddings);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static ReadOnlyMemory<float> Embed(string text)
    {
        var vector = new float[DimensionCount];
        foreach (var token in Tokenize(text))
        {
            var hash = Fnv1a(token);
            var bucket = (int)(hash % DimensionCount);
            var sign = (hash & 1) == 0 ? 1f : -1f;
            vector[bucket] += sign;
        }

        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(token => token.Length > 0);
    }

    private static uint Fnv1a(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var ch in token)
        {
            hash = unchecked((hash ^ ch) * prime);
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        var sumOfSquares = 0.0;
        foreach (var value in vector)
        {
            sumOfSquares += value * value;
        }

        if (sumOfSquares <= 0.0)
        {
            return;
        }

        var inverseNorm = (float)(1.0 / Math.Sqrt(sumOfSquares));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= inverseNorm;
        }
    }
}
