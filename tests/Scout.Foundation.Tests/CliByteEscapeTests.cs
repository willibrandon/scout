namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible CLI byte escaping.
/// </summary>
public sealed class CliByteEscapeTests
{
    /// <summary>
    /// Verifies escaping follows bstr's printable, special, and hexadecimal byte rules.
    /// </summary>
    [Fact]
    public void EscapeMatchesBstrByteRules()
    {
        Assert.Equal(string.Empty, CliByteEscape.Escape([]));
        Assert.Equal("abc", CliByteEscape.Escape("abc"u8));
        Assert.Equal(@"\\", CliByteEscape.Escape([(byte)'\\']));
        Assert.Equal(@"\0\n\t\r", CliByteEscape.Escape([0, (byte)'\n', (byte)'\t', (byte)'\r']));
        Assert.Equal(@"\x20!\x7F~", CliByteEscape.Escape([(byte)' ', (byte)'!', 0x7F, (byte)'~']));
        Assert.Equal("a\u2603b", CliByteEscape.Escape("a\u2603b"u8));
        Assert.Equal(@"\xE2(\xA1", CliByteEscape.Escape([0xE2, (byte)'(', 0xA1]));
        Assert.Equal(@"a\xFFb", CliByteEscape.Escape([(byte)'a', 0xFF, (byte)'b']));
    }

    /// <summary>
    /// Verifies unescaping decodes the escape forms accepted by ripgrep.
    /// </summary>
    [Fact]
    public void UnescapeDecodesRipgrepEscapeForms()
    {
        Assert.Equal([], CliByteEscape.Unescape(string.Empty));
        Assert.Equal([(byte)'\\'], CliByteEscape.Unescape(@"\\"));
        Assert.Equal([0, (byte)'\n', (byte)'\t', (byte)'\r'], CliByteEscape.Unescape(@"\0\n\t\r"));
        Assert.Equal([0xFF, 0x7F], CliByteEscape.Unescape(@"\xFF\x7f"));
        Assert.Equal("a\u2603b"u8.ToArray(), CliByteEscape.Unescape("a\u2603b"));
    }

    /// <summary>
    /// Verifies invalid and incomplete escapes are preserved literally.
    /// </summary>
    [Fact]
    public void UnescapePreservesInvalidEscapeForms()
    {
        Assert.Equal(@"\"u8.ToArray(), CliByteEscape.Unescape(@"\"));
        Assert.Equal(@"\x"u8.ToArray(), CliByteEscape.Unescape(@"\x"));
        Assert.Equal(@"\xF"u8.ToArray(), CliByteEscape.Unescape(@"\xF"));
        Assert.Equal(@"\xGG"u8.ToArray(), CliByteEscape.Unescape(@"\xGG"));
        Assert.Equal(@"\u{2603}"u8.ToArray(), CliByteEscape.Unescape(@"\u{2603}"));
        Assert.Equal(@"\a\b\f\v"u8.ToArray(), CliByteEscape.Unescape(@"\a\b\f\v"));
    }

    /// <summary>
    /// Verifies byte-oriented unescaping preserves raw bytes outside valid escape sequences.
    /// </summary>
    [Fact]
    public void UnescapeByteInputPreservesRawInvalidUtf8()
    {
        byte[] escaped = [(byte)'\\', 0xFF, (byte)'x', (byte)'\\', (byte)'x', (byte)'F', (byte)'F'];

        byte[] unescaped = CliByteEscape.Unescape(escaped);

        Assert.Equal([(byte)'\\', 0xFF, (byte)'x', 0xFF], unescaped);
    }
}
