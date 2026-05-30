namespace Scout;

internal readonly struct ContextLineInfo
{
    public ContextLineInfo(
        int start,
        int length,
        long lineNumber,
        bool selectedMatch,
        long matchColumn,
        bool originalMatch,
        long contextColumn)
    {
        Start = start;
        Length = length;
        LineNumber = lineNumber;
        SelectedMatch = selectedMatch;
        MatchColumn = matchColumn;
        OriginalMatch = originalMatch;
        ContextColumn = contextColumn;
    }

    public int Start { get; }

    public int Length { get; }

    public long LineNumber { get; }

    public bool SelectedMatch { get; }

    public long MatchColumn { get; }

    public bool OriginalMatch { get; }

    public long ContextColumn { get; }
}
