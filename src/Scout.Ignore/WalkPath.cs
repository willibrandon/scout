using System;
using System.Text;

namespace Scout;

internal readonly struct WalkPath
{
    private static readonly Encoding Utf8Lossy = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly byte[]? unixPathBytes;
    private readonly byte[]? unixFileNameBytes;

    private WalkPath(string textPath, byte[]? unixPathBytes, byte[]? unixFileNameBytes)
    {
        TextPath = textPath;
        this.unixPathBytes = unixPathBytes;
        this.unixFileNameBytes = unixFileNameBytes;
    }

    public string TextPath { get; }

    public ReadOnlySpan<byte> UnixPathBytes => unixPathBytes.AsSpan();

    public ReadOnlySpan<byte> UnixFileNameBytes => unixFileNameBytes.AsSpan();

    public bool IsRawUnixPath => unixPathBytes is not null;

    public static WalkPath FromText(string textPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(textPath);
        return new WalkPath(textPath, unixPathBytes: null, unixFileNameBytes: null);
    }

    public static WalkPath FromRawUnix(ReadOnlySpan<byte> unixPathBytes, ReadOnlySpan<byte> unixFileNameBytes)
    {
        if (unixPathBytes.IsEmpty)
        {
            throw new ArgumentException("Path cannot be empty.", nameof(unixPathBytes));
        }

        return new WalkPath(
            Utf8Lossy.GetString(unixPathBytes),
            unixPathBytes.ToArray(),
            unixFileNameBytes.ToArray());
    }
}
