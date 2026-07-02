namespace Scout;

internal sealed class LineFlushingMemoryStream : MemoryStream
{
    private const int MinimumRetainedCapacity = 256;

    private readonly RawByteWriter output;
    private readonly object outputLock;
    private readonly byte lineTerminator;
    private readonly int lineFlushThreshold;
    private readonly int maxRetainedCapacity;

    public LineFlushingMemoryStream(
        RawByteWriter output,
        object outputLock,
        byte lineTerminator,
        int lineFlushThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lineFlushThreshold, 1);

        this.output = output;
        this.outputLock = outputLock;
        this.lineTerminator = lineTerminator;
        this.lineFlushThreshold = lineFlushThreshold;
        maxRetainedCapacity = Math.Max(MinimumRetainedCapacity, lineFlushThreshold * 4);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
        FlushCompleteLinesIfThresholdExceeded();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        FlushCompleteLinesIfThresholdExceeded();
    }

    public override void Flush()
    {
        FlushToOutput();
    }

    private void FlushCompleteLinesIfThresholdExceeded()
    {
        if (Length < lineFlushThreshold)
        {
            return;
        }

        if (!TryGetBuffer(out ArraySegment<byte> segment))
        {
            FlushToOutput();
            return;
        }

        ReadOnlySpan<byte> bytes = segment.AsSpan(0, checked((int)Length));
        int terminatorOffset = bytes.LastIndexOf(lineTerminator);
        if (terminatorOffset < 0)
        {
            return;
        }

        int flushLength = terminatorOffset + 1;
        WriteToOutput(bytes[..flushLength]);
        int remaining = checked((int)Length) - flushLength;
        if (remaining > 0)
        {
            segment.AsSpan(flushLength, remaining).CopyTo(segment.AsSpan(0, remaining));
        }

        Position = remaining;
        SetLength(remaining);
        TrimOversizedEmptyBuffer();
    }

    private void FlushToOutput()
    {
        if (Length == 0)
        {
            return;
        }

        if (TryGetBuffer(out ArraySegment<byte> segment))
        {
            WriteToOutput(segment.AsSpan(0, checked((int)Length)));
        }
        else
        {
            WriteToOutput(ToArray());
        }

        Position = 0;
        SetLength(0);
        TrimOversizedEmptyBuffer();
    }

    private void WriteToOutput(ReadOnlySpan<byte> bytes)
    {
        lock (outputLock)
        {
            output.Write(bytes);
        }
    }

    private void TrimOversizedEmptyBuffer()
    {
        if (Length == 0 && Capacity > maxRetainedCapacity)
        {
            Capacity = 0;
        }
    }
}
