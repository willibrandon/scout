using System;
using System.IO;

namespace Scout;

internal sealed class BufferedSearchOutput : IDisposable
{
    private readonly MemoryStream stream;

    public BufferedSearchOutput(MemoryStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
    }

    public long Length => stream.Length;

    public void WriteTo(RawByteWriter output)
    {
        if (stream.TryGetBuffer(out ArraySegment<byte> segment))
        {
            output.Write(segment.AsSpan(0, checked((int)stream.Length)));
            return;
        }

        output.Write(stream.ToArray());
    }

    public void Dispose()
    {
        stream.Dispose();
    }
}
