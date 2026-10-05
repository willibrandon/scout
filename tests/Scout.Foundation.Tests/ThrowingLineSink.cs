namespace Scout;

internal struct ThrowingLineSink : ILineSink
{
    internal int Callbacks { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        Callbacks++;
        throw new InvalidOperationException("Callback failed.");
    }
}
