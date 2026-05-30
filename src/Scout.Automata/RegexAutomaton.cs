using System;

namespace Scout;

/// <summary>
/// Executes a byte-oriented regular expression automaton.
/// </summary>
public sealed class RegexAutomaton
{
    private readonly RegexNfa nfa;

    private RegexAutomaton(RegexNfa nfa)
    {
        this.nfa = nfa;
    }

    /// <summary>
    /// Compiles a regex pattern into a Thompson NFA-backed automaton.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(ReadOnlySpan<byte> pattern)
    {
        return Compile(pattern, caseInsensitive: false, multiLine: false, dotMatchesNewline: false);
    }

    /// <summary>
    /// Compiles a regex pattern into a Thompson NFA-backed automaton with root regex options.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(ReadOnlySpan<byte> pattern, bool multiLine, bool dotMatchesNewline)
    {
        return Compile(pattern, caseInsensitive: false, multiLine, dotMatchesNewline);
    }

    /// <summary>
    /// Compiles a regex pattern into a Thompson NFA-backed automaton with root regex options.
    /// </summary>
    /// <param name="pattern">The regex pattern bytes.</param>
    /// <param name="caseInsensitive">Whether literal and class atoms match ASCII case-insensitively.</param>
    /// <param name="multiLine">Whether <c>^</c> and <c>$</c> match adjacent to line feeds.</param>
    /// <param name="dotMatchesNewline">Whether <c>.</c> matches line feeds.</param>
    /// <param name="crlf">Whether CRLF mode treats carriage returns and line feeds as line terminators.</param>
    /// <param name="lineTerminator">The line terminator byte used when CRLF mode is disabled.</param>
    /// <returns>The compiled automaton.</returns>
    public static RegexAutomaton Compile(
        ReadOnlySpan<byte> pattern,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf = false,
        byte lineTerminator = (byte)'\n')
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(pattern);
        return new RegexAutomaton(RegexNfaCompiler.Compile(
            tree.Root,
            new RegexCompileOptions(caseInsensitive, swapGreed: false, multiLine, dotMatchesNewline, crlf, lineTerminator)));
    }

    /// <summary>
    /// Finds the first match in a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack, startAt: 0);
    }

    /// <summary>
    /// Finds the first match in a haystack at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <param name="startAt">The first byte offset to consider.</param>
    /// <returns>The first match, or <see langword="null" /> when no match exists.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        var vm = new PikeVm(nfa);
        for (int start = Math.Clamp(startAt, 0, haystack.Length); start <= haystack.Length; start++)
        {
            if (vm.TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a value indicating whether the regex matches a haystack.
    /// </summary>
    /// <param name="haystack">The haystack bytes.</param>
    /// <returns><see langword="true" /> when a match exists.</returns>
    public bool IsMatch(ReadOnlySpan<byte> haystack)
    {
        return Find(haystack).HasValue;
    }
}
