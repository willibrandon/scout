using System;
using System.IO;

namespace Scout;

internal sealed class SegmentedReadStream(params byte[][] segments) : Stream
{
    private int _segmentIndex;
    private int _segmentOffset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (_segmentIndex < segments.Length && _segmentOffset == segments[_segmentIndex].Length)
        {
            _segmentIndex++;
            _segmentOffset = 0;
        }

        if (_segmentIndex >= segments.Length)
        {
            return 0;
        }

        byte[] segment = segments[_segmentIndex];
        int copied = Math.Min(count, segment.Length - _segmentOffset);
        segment.AsSpan(_segmentOffset, copied).CopyTo(buffer.AsSpan(offset, copied));
        _segmentOffset += copied;
        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
