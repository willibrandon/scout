namespace Scout;

internal struct RegexPlanCountingLineSink<TSink> : ILineSink
    where TSink : struct, ILineSink
{
    private TSink inner;
    private readonly IReadOnlyList<byte[]> needles;
    private readonly RegexSearchPlan? regexPlan;
    private readonly bool asciiCaseInsensitive;
    private readonly bool lineRegexp;
    private readonly bool wordRegexp;
    private readonly bool crlf;
    private readonly bool nullData;
    private readonly bool countMatches;

    public RegexPlanCountingLineSink(
        TSink inner,
        IReadOnlyList<byte[]> needles,
        RegexSearchPlan? regexPlan,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        bool countMatches)
    {
        this.inner = inner;
        this.needles = needles;
        this.regexPlan = regexPlan;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.lineRegexp = lineRegexp;
        this.wordRegexp = wordRegexp;
        this.crlf = crlf;
        this.nullData = nullData;
        this.countMatches = countMatches;
        MatchedLines = 0;
        Matches = 0;
    }

    public readonly TSink Inner => inner;

    public ulong MatchedLines { get; private set; }

    public long Matches { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        inner.MatchedLine(lineNumber, byteOffset, matchColumn, line);
        MatchedLines++;
        if (countMatches)
        {
            Matches += LiteralLineSearcher.CountLineMatchesWithRegexPlan(line, needles, regexPlan, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
        }
    }
}
