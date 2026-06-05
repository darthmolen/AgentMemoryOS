using System.Text;

namespace AgentMemoryOS.Abstractions;

/// <summary>
/// Text utilities shared by every backing store so that fact identity and dedup behave
/// identically regardless of which store is in use.
/// </summary>
public static class MemoryText
{
    /// <summary>
    /// Produces a normalized identity key for a piece of content: lower-cased, punctuation
    /// removed, and internal whitespace collapsed to single spaces. Two statements that
    /// differ only in case or punctuation share a key and are treated as the same fact.
    /// </summary>
    /// <param name="text">The content to normalize.</param>
    /// <returns>The normalized key, or an empty string when the content has no word characters.</returns>
    public static string NormalizeKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lowered = text.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        var previousWasSpace = false;
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
            else if (char.IsWhiteSpace(ch) && !previousWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
