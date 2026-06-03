namespace Scout;

internal readonly struct LargeFileSegmentMatch
{
    public LargeFileSegmentMatch(long lineNumber, int lineStart, int lineLength, long matchColumn)
    {
        LineNumber = lineNumber;
        LineStart = lineStart;
        LineLength = lineLength;
        MatchColumn = matchColumn;
    }

    public long LineNumber { get; }

    public int LineStart { get; }

    public int LineLength { get; }

    public long MatchColumn { get; }
}
