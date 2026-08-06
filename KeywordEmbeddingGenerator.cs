using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace RoutingChat;

/// <summary>
/// A deterministic, offline stand-in for a real embedding model.
/// </summary>
/// <remarks>
/// Words are hashed into a fixed-width vector and the result is L2-normalized, so cosine
/// similarity reflects vocabulary overlap. That is enough for <see cref="SemanticRoutingChatClient"/>
/// to route convincingly without a network call or an API key. Swap it for a real
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> and nothing else in the sample changes.
/// </remarks>
internal sealed partial class KeywordEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 192;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = [];
        foreach (string value in values)
        {
            embeddings.Add(new Embedding<float>(Embed(value)));
        }

        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Nothing to release.
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (Match match in WordPattern().Matches(text))
        {
            string word = Stem(match.Value.ToLowerInvariant());
            if (word.Length < 3)
            {
                continue;
            }

            // Two slots per word so unrelated words are less likely to fully collide.
            int hash = StableHash(word);
            vector[(hash & int.MaxValue) % Dimensions] += 1f;
            vector[((hash * 31) & int.MaxValue) % Dimensions] += 0.5f;
        }

        float magnitude = 0f;
        foreach (float component in vector)
        {
            magnitude += component * component;
        }

        magnitude = MathF.Sqrt(magnitude);
        if (magnitude > 0f)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }

    /// <summary>Collapses the most common English suffixes so "testing" and "tests" agree.</summary>
    private static string Stem(string word)
    {
        foreach (string suffix in (string[])["ing", "ed", "es", "s"])
        {
            if (word.Length > suffix.Length + 2 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    /// <summary>FNV-1a, so vectors do not change between runs the way string.GetHashCode would.</summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return (int)hash;
        }
    }

    [GeneratedRegex(@"[a-zA-Z']+")]
    private static partial Regex WordPattern();
}
