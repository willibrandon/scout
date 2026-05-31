using System;
using System.IO;
using System.IO.Compression;

namespace Scout;

internal static class GenerateOutput
{
    private static readonly Lazy<byte[]> ManBytes = new(() => Inflate(GeneratedManPageArtifact.CompressedBase64));
    private static readonly Lazy<byte[]> CompleteBashBytes = new(() => Inflate(GeneratedCompleteBashArtifact.CompressedBase64));
    private static readonly Lazy<byte[]> CompleteZshBytes = new(() => Inflate(GeneratedCompleteZshArtifact.CompressedBase64));
    private static readonly Lazy<byte[]> CompleteFishBytes = new(() => Inflate(GeneratedCompleteFishArtifact.CompressedBase64));
    private static readonly Lazy<byte[]> CompletePowerShellBytes = new(() => Inflate(GeneratedCompletePowerShellArtifact.CompressedBase64));

    internal static ReadOnlySpan<byte> Get(CliGenerateMode mode)
    {
        switch (mode)
        {
            case CliGenerateMode.Man:
                return ManBytes.Value;

            case CliGenerateMode.CompleteBash:
                return CompleteBashBytes.Value;

            case CliGenerateMode.CompleteZsh:
                return CompleteZshBytes.Value;

            case CliGenerateMode.CompleteFish:
                return CompleteFishBytes.Value;

            case CliGenerateMode.CompletePowerShell:
                return CompletePowerShellBytes.Value;

            default:
                return [];
        }
    }

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
