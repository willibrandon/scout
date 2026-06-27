namespace Scout;

internal struct RegexPlanCountingMatchLineSink<TSink> : IMatchLineSink
    where TSink : struct, IMatchLineSink
{
    private TSink inner;
    private long lastMatchedLineNumber;

    public RegexPlanCountingMatchLineSink(TSink inner)
    {
        this.inner = inner;
        lastMatchedLineNumber = 0;
        MatchedLines = 0;
        Matches = 0;
    }

    public readonly TSink Inner => inner;

    public ulong MatchedLines { get; private set; }

    public long Matches { get; private set; }

    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        inner.MatchedLine(lineNumber, lineByteOffset, matchByteOffset, matchColumn, line, match);
        Matches++;
        if (lineNumber != lastMatchedLineNumber)
        {
            MatchedLines++;
            lastMatchedLineNumber = lineNumber;
        }
    }

    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        inner.FinishLine(lineNumber, lineByteOffset, line);
    }
}
