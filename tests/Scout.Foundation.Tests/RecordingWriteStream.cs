
namespace Scout;

internal sealed class RecordingWriteStream : MemoryStream
{
    internal int FlushCount { get; private set; }

    public override void Flush()
    {
        FlushCount++;
        base.Flush();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
    }
}
