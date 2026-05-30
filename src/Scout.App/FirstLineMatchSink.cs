using System;

namespace Scout;

internal struct FirstLineMatchSink : ILineSink
{
    public long MatchColumn { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchColumn = matchColumn;
    }
}
