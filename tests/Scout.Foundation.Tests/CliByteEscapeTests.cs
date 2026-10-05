namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible CLI byte escaping.
/// </summary>
[TestClass]
public sealed class CliByteEscapeTests
{
    /// <summary>
    /// Verifies escaping follows bstr's printable, special, and hexadecimal byte rules.
    /// </summary>
    [TestMethod]
    public void EscapeMatchesBstrByteRules()
    {
        Assert.AreEqual(string.Empty, CliByteEscape.Escape([]));
        Assert.AreEqual("abc", CliByteEscape.Escape("abc"u8));
        Assert.AreEqual(@"\\", CliByteEscape.Escape([(byte)'\\']));
        Assert.AreEqual(@"\0\n\t\r", CliByteEscape.Escape([0, (byte)'\n', (byte)'\t', (byte)'\r']));
        Assert.AreEqual(@"\x20!\x7F~", CliByteEscape.Escape([(byte)' ', (byte)'!', 0x7F, (byte)'~']));
        Assert.AreEqual("a\u2603b", CliByteEscape.Escape("a\u2603b"u8));
        Assert.AreEqual(@"\xE2(\xA1", CliByteEscape.Escape([0xE2, (byte)'(', 0xA1]));
        Assert.AreEqual(@"a\xFFb", CliByteEscape.Escape([(byte)'a', 0xFF, (byte)'b']));
    }

    /// <summary>
    /// Verifies unescaping decodes the escape forms accepted by ripgrep.
    /// </summary>
    [TestMethod]
    public void UnescapeDecodesRipgrepEscapeForms()
    {
        Assert.AreSequenceEqual<byte>([], CliByteEscape.Unescape(string.Empty));
        Assert.AreSequenceEqual<byte>([(byte)'\\'], CliByteEscape.Unescape(@"\\"));
        Assert.AreSequenceEqual<byte>([0, (byte)'\n', (byte)'\t', (byte)'\r'], CliByteEscape.Unescape(@"\0\n\t\r"));
        Assert.AreSequenceEqual<byte>([0xFF, 0x7F], CliByteEscape.Unescape(@"\xFF\x7f"));
        Assert.AreSequenceEqual("a\u2603b"u8.ToArray(), CliByteEscape.Unescape("a\u2603b"));
    }

    /// <summary>
    /// Verifies invalid and incomplete escapes are preserved literally.
    /// </summary>
    [TestMethod]
    public void UnescapePreservesInvalidEscapeForms()
    {
        Assert.AreSequenceEqual(@"\"u8.ToArray(), CliByteEscape.Unescape(@"\"));
        Assert.AreSequenceEqual(@"\x"u8.ToArray(), CliByteEscape.Unescape(@"\x"));
        Assert.AreSequenceEqual(@"\xF"u8.ToArray(), CliByteEscape.Unescape(@"\xF"));
        Assert.AreSequenceEqual(@"\xGG"u8.ToArray(), CliByteEscape.Unescape(@"\xGG"));
        Assert.AreSequenceEqual(@"\u{2603}"u8.ToArray(), CliByteEscape.Unescape(@"\u{2603}"));
        Assert.AreSequenceEqual(@"\a\b\f\v"u8.ToArray(), CliByteEscape.Unescape(@"\a\b\f\v"));
    }

    /// <summary>
    /// Verifies byte-oriented unescaping preserves raw bytes outside valid escape sequences.
    /// </summary>
    [TestMethod]
    public void UnescapeByteInputPreservesRawInvalidUtf8()
    {
        byte[] escaped = [(byte)'\\', 0xFF, (byte)'x', (byte)'\\', (byte)'x', (byte)'F', (byte)'F'];

        byte[] unescaped = CliByteEscape.Unescape(escaped);

        Assert.AreSequenceEqual<byte>([(byte)'\\', 0xFF, (byte)'x', 0xFF], unescaped);
    }
}
