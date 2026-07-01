using System.Text;

namespace Scout.Text.Regex;

/// <summary>
/// Represents an ordered set of compiled byte regex patterns.
/// </summary>
public sealed class ByteRegexSet
{
    private readonly PatternSet patternSet;

    private ByteRegexSet(PatternSet patternSet)
    {
        this.patternSet = patternSet;
    }

    /// <summary>
    /// Gets the number of patterns in the set.
    /// </summary>
    public int Count => patternSet.Count;

    /// <summary>
    /// Compiles an ordered set of byte regex patterns.
    /// </summary>
    /// <param name="patterns">The ordered pattern bytes.</param>
    /// <param name="options">The compile options, or <see langword="null" /> for defaults.</param>
    /// <returns>The compiled set.</returns>
    /// <exception cref="ByteRegexParseException">Thrown when a pattern is not syntactically valid.</exception>
    public static ByteRegexSet Compile(IReadOnlyList<byte[]> patterns, ByteRegexOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ByteRegexOptions resolvedOptions = options ?? new ByteRegexOptions();
        try
        {
            return new ByteRegexSet(PatternSet.Compile(
                patterns,
                resolvedOptions.AsciiCaseInsensitive,
                resolvedOptions.MultiLine,
                resolvedOptions.DotMatchesNewline,
                resolvedOptions.Crlf,
                resolvedOptions.LineTerminator,
                resolvedOptions.Utf8,
                resolvedOptions.UnicodeClasses,
                resolvedOptions.DfaSizeLimit,
                resolvedOptions.ToSpecializationMode()));
        }
        catch (FormatException exception)
        {
            throw ByteRegexParseException.FromFormatException(exception);
        }
    }

    /// <summary>
    /// Compiles ordered UTF-16 pattern strings as UTF-8 bytes.
    /// </summary>
    /// <param name="patterns">The ordered pattern strings.</param>
    /// <param name="options">The compile options, or <see langword="null" /> for defaults.</param>
    /// <returns>The compiled set.</returns>
    /// <exception cref="ByteRegexParseException">Thrown when a pattern is not syntactically valid.</exception>
    public static ByteRegexSet Compile(IEnumerable<string> patterns, ByteRegexOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var encoded = new List<byte[]>();
        foreach (string pattern in patterns)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            encoded.Add(Encoding.UTF8.GetBytes(pattern));
        }

        return Compile(encoded, options);
    }

    /// <summary>
    /// Returns a value indicating whether any pattern matches an input.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> input)
    {
        return patternSet.Find(input).HasValue;
    }

    /// <summary>
    /// Finds the first set match in an input at or after a byte offset.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first set match, or <see langword="null" /> when no match exists.</returns>
    public ByteRegexSetMatch? Find(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startAt);
        PatternSetMatch? match = patternSet.Find(input, startAt);
        return match.HasValue
            ? new ByteRegexSetMatch(match.Value.PatternId, ByteRegexMatch.FromRegexMatch(match.Value.Match))
            : null;
    }

    /// <summary>
    /// Counts all non-overlapping matches for any pattern in an input at or after a byte offset.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The match count.</returns>
    public long CountMatches(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startAt);
        return patternSet.CountMatches(input, startAt);
    }

    /// <summary>
    /// Finds the first whole-pattern capture synthesized by the set.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The captures, or <see langword="null" /> when no match exists.</returns>
    public ByteRegexCaptures? FindCaptures(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startAt);
        RegexCaptures? captures = patternSet.FindCaptures(input, startAt);
        return captures is null ? null : new ByteRegexCaptures(captures);
    }
}
