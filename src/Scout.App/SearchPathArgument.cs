using System;
using System.Text;

namespace Scout;

internal readonly struct SearchPathArgument
{
    private static readonly Encoding Utf8Lossy = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private SearchPathArgument(string? text, byte[]? unixBytes, byte[] displayBytes)
    {
        Text = text;
        UnixBytes = unixBytes;
        DisplayBytes = displayBytes;
    }

    public string? Text { get; }

    public byte[]? UnixBytes { get; }

    public byte[] DisplayBytes { get; }

    public bool IsRawUnixPath => UnixBytes is not null;

    public string DisplayText => Text ?? Utf8Lossy.GetString(DisplayBytes);

    public static SearchPathArgument FromText(string text, byte[] displayBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(displayBytes);
        return new SearchPathArgument(text, unixBytes: null, displayBytes);
    }

    public static SearchPathArgument FromUnixBytes(ReadOnlySpan<byte> unixBytes, byte[] displayBytes)
    {
        ArgumentNullException.ThrowIfNull(displayBytes);
        return new SearchPathArgument(text: null, unixBytes.ToArray(), displayBytes);
    }
}
