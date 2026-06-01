using System;

namespace Scout;

internal struct CapturingLineSink : ILineSink
{
    public ulong MatchedLines { get; private set; }

    public long LineNumber { get; private set; }

    public byte[] Line { get; private set; }

    public ulong ContextLines { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
        LineNumber = lineNumber;
        Line = line.ToArray();
    }

    public void ContextLine(long lineNumber, long byteOffset, long contextColumn, ReadOnlySpan<byte> line)
    {
        ContextLines++;
    }
}
