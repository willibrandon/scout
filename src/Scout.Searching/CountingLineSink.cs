using System;

namespace Scout;

internal struct CountingLineSink : ILineSink
{
    public ulong MatchedLines { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
    }
}
