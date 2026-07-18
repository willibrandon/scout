namespace Scout;

/// <summary>
/// Buffers parallel output and flushes only complete records to a shared writer.
/// </summary>
/// <param name="output">The shared output writer.</param>
/// <param name="outputLock">The lock that serializes writes to <paramref name="output" />.</param>
/// <param name="lineTerminator">The byte that terminates one output record.</param>
/// <param name="lineFlushThreshold">The buffered-byte threshold that initiates a complete-record flush.</param>
internal sealed class LineFlushingMemoryStream(
    RawByteWriter output,
    object outputLock,
    byte lineTerminator,
    int lineFlushThreshold) : MemoryStream
{
    private const int MinimumRetainedCapacity = 256;

    private readonly RawByteWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly object _outputLock = outputLock ?? throw new ArgumentNullException(nameof(outputLock));
    private readonly byte _lineTerminator = lineTerminator;
    private readonly int _lineFlushThreshold = ValidateLineFlushThreshold(lineFlushThreshold);
    private readonly int _maxRetainedCapacity = GetMaxRetainedCapacity(lineFlushThreshold);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _ = buffer.AsSpan(offset, count);
        FlushCompleteLinesBeforeThresholdCrossing(count);
        base.Write(buffer, offset, count);
        FlushCompleteLinesIfThresholdExceeded();
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        FlushCompleteLinesBeforeThresholdCrossing(buffer.Length);
        base.Write(buffer);
        FlushCompleteLinesIfThresholdExceeded();
    }

    /// <inheritdoc />
    public override void Flush()
    {
        FlushToOutput();
    }

    private void FlushCompleteLinesBeforeThresholdCrossing(int incomingLength)
    {
        if (incomingLength == 0 ||
            Length == 0 ||
            Length + incomingLength <= _lineFlushThreshold)
        {
            return;
        }

        FlushCompleteLines();
    }

    private void FlushCompleteLinesIfThresholdExceeded()
    {
        if (Length < _lineFlushThreshold)
        {
            return;
        }

        FlushCompleteLines();
    }

    private void FlushCompleteLines()
    {
        if (!TryGetBuffer(out ArraySegment<byte> segment))
        {
            FlushToOutput();
            return;
        }

        ReadOnlySpan<byte> bytes = segment.AsSpan(0, checked((int)Length));
        int terminatorOffset = bytes.LastIndexOf(_lineTerminator);
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
        lock (_outputLock)
        {
            _output.Write(bytes);
        }
    }

    private void TrimOversizedEmptyBuffer()
    {
        if (Length == 0 && Capacity > _maxRetainedCapacity)
        {
            Capacity = 0;
        }
    }

    private static int ValidateLineFlushThreshold(int lineFlushThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lineFlushThreshold, 1);
        return lineFlushThreshold;
    }

    private static int GetMaxRetainedCapacity(int lineFlushThreshold)
    {
        return Math.Max(
            MinimumRetainedCapacity,
            checked(ValidateLineFlushThreshold(lineFlushThreshold) * 4));
    }
}
