using System.Text;

namespace Scout.Text.Regex;

/// <summary>
/// Represents a compiled byte-oriented regular expression. Instances are safe to share across threads for matching.
/// </summary>
public sealed class ByteRegex
{
    private readonly RegexAutomaton automaton;

    private ByteRegex(RegexAutomaton automaton)
    {
        this.automaton = automaton;
    }

    /// <summary>
    /// Compiles a byte regex pattern.
    /// </summary>
    /// <param name="pattern">The pattern bytes.</param>
    /// <param name="options">The compile options, or <see langword="null" /> for defaults.</param>
    /// <returns>The compiled regex.</returns>
    /// <exception cref="ByteRegexParseException">Thrown when the pattern is not syntactically valid.</exception>
    public static ByteRegex Compile(ReadOnlySpan<byte> pattern, ByteRegexOptions? options = null)
    {
        ByteRegexOptions resolvedOptions = options ?? new ByteRegexOptions();
        try
        {
            return new ByteRegex(RegexAutomaton.Compile(
                pattern,
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
    /// Compiles a UTF-16 pattern string as UTF-8 bytes.
    /// </summary>
    /// <param name="pattern">The pattern string.</param>
    /// <param name="options">The compile options, or <see langword="null" /> for defaults.</param>
    /// <returns>The compiled regex.</returns>
    /// <exception cref="ByteRegexParseException">Thrown when the pattern is not syntactically valid.</exception>
    public static ByteRegex Compile(string pattern, ByteRegexOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return Compile(Encoding.UTF8.GetBytes(pattern), options);
    }

    /// <summary>
    /// Returns a value indicating whether the regex matches an input.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> input)
    {
        return automaton.IsMatch(input);
    }

    /// <summary>
    /// Finds the first match in an input at or after a byte offset.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public ByteRegexMatch? Find(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ThrowIfNegative(startAt);
        RegexMatch? match = automaton.Find(input, startAt);
        return match.HasValue ? ByteRegexMatch.FromRegexMatch(match.Value) : null;
    }

    /// <summary>
    /// Finds the first match and participating capture groups in an input at or after a byte offset.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The captures, or <see langword="null" /> when no match exists.</returns>
    public ByteRegexCaptures? FindCaptures(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ThrowIfNegative(startAt);
        RegexCaptures? captures = automaton.FindCaptures(input, startAt);
        return captures is null ? null : new ByteRegexCaptures(captures);
    }

    /// <summary>
    /// Counts all non-overlapping matches in an input at or after a byte offset.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The match count.</returns>
    public long Count(ReadOnlySpan<byte> input, int startAt = 0)
    {
        ThrowIfNegative(startAt);
        return automaton.CountMatches(input, startAt);
    }

    /// <summary>
    /// Iterates all non-overlapping matches in an input.
    /// </summary>
    /// <typeparam name="TState">The caller-owned state type.</typeparam>
    /// <param name="input">The input bytes.</param>
    /// <param name="state">Caller-owned state passed by reference.</param>
    /// <param name="callback">The synchronous callback.</param>
    /// <returns>The number of reported matches.</returns>
    public int ForEachMatch<TState>(
        ReadOnlySpan<byte> input,
        ref TState state,
        ByteRegexMatchHandler<TState> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        int count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (offset <= input.Length)
        {
            RegexMatch? regexMatch = automaton.Find(input, offset);
            if (!regexMatch.HasValue)
            {
                return count;
            }

            var matcherMatch = new MatcherMatch(regexMatch.Value.Start, regexMatch.Value.Length);
            if (MatchIterator.IsSuppressedEmpty(matcherMatch, suppressedEmptyStart))
            {
                offset = MatchIterator.AdvanceAfterSuppressedEmpty(matcherMatch, input.Length);
                suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
                continue;
            }

            if (!callback(input, ByteRegexMatch.FromRegexMatch(regexMatch.Value), ref state))
            {
                return count;
            }

            count++;
            offset = MatchIterator.AdvanceAfterReported(matcherMatch, input.Length, ref suppressedEmptyStart);
        }

        return count;
    }

    private static void ThrowIfNegative(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
    }
}
