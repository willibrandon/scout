using System;

namespace Scout;

internal struct LargeFileSegmentMatchSink : ILineSink
{
    private List<LargeFileSegmentMatch>? matches;

    public ulong MatchedLines { get; private set; }

    public List<LargeFileSegmentMatch>? Matches => matches;

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
        matches ??= [];
        matches.Add(new LargeFileSegmentMatch(lineNumber, checked((int)byteOffset), line.Length, matchColumn));
    }
}
