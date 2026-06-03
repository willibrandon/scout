
namespace Scout;

internal struct CapturingMatchLineSink : IMatchLineSink
{
    public ulong Matches { get; private set; }

    public long LineNumber { get; private set; }

    public long LineByteOffset { get; private set; }

    public long MatchByteOffset { get; private set; }

    public long MatchColumn { get; private set; }

    public byte[] Match { get; private set; }

    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _ = line;
        Matches++;
        LineNumber = lineNumber;
        LineByteOffset = lineByteOffset;
        MatchByteOffset = matchByteOffset;
        MatchColumn = matchColumn;
        Match = match.ToArray();
    }
}
