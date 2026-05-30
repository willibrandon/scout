using System.IO;

namespace Scout;

internal static class FuzzInput
{
    internal static byte[] ReadAll(Stream stream)
    {
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
