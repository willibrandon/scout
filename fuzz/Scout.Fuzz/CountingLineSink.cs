using System;

namespace Scout;

internal struct CountingLineSink : ILineSink
{
    private ulong matchedLines;
    private long checksum;

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        matchedLines++;
        checksum ^= lineNumber;
        checksum ^= byteOffset << 7;
        checksum ^= matchColumn << 13;
        checksum ^= line.Length << 19;
    }

    internal readonly ulong MatchedLines => matchedLines;

    internal readonly long Checksum => checksum;
}
