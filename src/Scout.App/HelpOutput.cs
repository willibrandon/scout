using System.IO.Compression;

namespace Scout;

internal static class HelpOutput
{
    private static readonly Lazy<byte[]> ShortBytes = new(() => GeneratedTextOutput.ForCurrentPlatform(Inflate(GeneratedShortHelpArtifact.CompressedBase64)));
    private static readonly Lazy<byte[]> LongBytes = new(() => GeneratedTextOutput.ForCurrentPlatform(Inflate(GeneratedLongHelpArtifact.CompressedBase64)));

    internal static ReadOnlySpan<byte> Short => ShortBytes.Value;

    internal static ReadOnlySpan<byte> Long => LongBytes.Value;

    private static byte[] Inflate(string base64)
    {
        byte[] compressed = Convert.FromBase64String(base64);
        using var compressedStream = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
