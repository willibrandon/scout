namespace Scout;

/// <summary>
/// Counts selected lines and their authoritative non-overlapping regex matches.
/// </summary>
/// <typeparam name="TSink">The wrapped line-sink type.</typeparam>
/// <param name="inner">The line sink that receives selected lines.</param>
/// <param name="needles">The ordered byte regex patterns.</param>
/// <param name="regexPlan">The reusable regex search plan.</param>
/// <param name="asciiCaseInsensitive">Whether ASCII letters compare case-insensitively.</param>
/// <param name="lineRegexp">Whether each pattern must match a full line.</param>
/// <param name="wordRegexp">Whether each match must have word boundaries.</param>
/// <param name="crlf">Whether CRLF is the line terminator.</param>
/// <param name="nullData">Whether NUL is the line terminator.</param>
/// <param name="countMatches">Whether authoritative matches should be counted.</param>
internal struct RegexPlanCountingLineSink<TSink>(
    TSink inner,
    IReadOnlyList<byte[]> needles,
    RegexSearchPlan? regexPlan,
    bool asciiCaseInsensitive,
    bool lineRegexp,
    bool wordRegexp,
    bool crlf,
    bool nullData,
    bool countMatches) : ILineSink
    where TSink : struct, ILineSink
{
    private TSink _inner = inner;
    private readonly IReadOnlyList<byte[]> _needles = needles;
    private readonly RegexSearchPlan? _regexPlan = regexPlan;
    private readonly bool _asciiCaseInsensitive = asciiCaseInsensitive;
    private readonly bool _lineRegexp = lineRegexp;
    private readonly bool _wordRegexp = wordRegexp;
    private readonly bool _crlf = crlf;
    private readonly bool _nullData = nullData;
    private readonly bool _countMatches = countMatches;

    /// <summary>
    /// Gets the wrapped line sink after search completion.
    /// </summary>
    public readonly TSink Inner => _inner;

    /// <summary>
    /// Gets the number of selected lines.
    /// </summary>
    public ulong MatchedLines { get; private set; }

    /// <summary>
    /// Gets the number of authoritative non-overlapping matches.
    /// </summary>
    public long Matches { get; private set; }

    /// <summary>
    /// Forwards and counts one selected line.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchColumn">The one-based byte column of the first match.</param>
    /// <param name="line">The selected line bytes.</param>
    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        _inner.MatchedLine(lineNumber, byteOffset, matchColumn, line);
        MatchedLines++;
        if (_countMatches)
        {
            Matches += LiteralLineSearcher.CountLineMatchesWithRegexPlan(
                line,
                _needles,
                _regexPlan,
                _asciiCaseInsensitive,
                _lineRegexp,
                _wordRegexp,
                _crlf,
                _nullData);
        }
    }
}
