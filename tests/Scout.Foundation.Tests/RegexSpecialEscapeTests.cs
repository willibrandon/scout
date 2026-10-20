namespace Scout;

/// <summary>
/// Verifies the shared decoder for the named special escapes regex-syntax accepts.
/// </summary>
[TestClass]
public sealed class RegexSpecialEscapeTests
{
    /// <summary>
    /// Verifies each special escape decodes to the byte regex-syntax assigns it.
    /// </summary>
    /// <param name="escaped">The byte written after the backslash.</param>
    /// <param name="expected">The decoded byte.</param>
    [TestMethod]
    [DataRow((byte)'a', (byte)0x07)]
    [DataRow((byte)'f', (byte)0x0C)]
    [DataRow((byte)'t', (byte)0x09)]
    [DataRow((byte)'n', (byte)0x0A)]
    [DataRow((byte)'r', (byte)0x0D)]
    [DataRow((byte)'v', (byte)0x0B)]
    public void SpecialEscapesDecode(byte escaped, byte expected)
    {
        Assert.IsTrue(RegexSpecialEscape.TryDecode(escaped, out byte value));
        Assert.AreEqual(expected, value);
    }

    /// <summary>
    /// Verifies bytes outside the special set are left for the caller to interpret.
    /// </summary>
    /// <param name="escaped">The byte written after the backslash.</param>
    [TestMethod]
    [DataRow((byte)'A')]
    [DataRow((byte)'V')]
    [DataRow((byte)'b')]
    [DataRow((byte)'d')]
    [DataRow((byte)'x')]
    [DataRow((byte)'\\')]
    [DataRow((byte)'-')]
    [DataRow((byte)'0')]
    [DataRow((byte)0x07)]
    public void OtherEscapesAreNotSpecial(byte escaped)
    {
        Assert.IsFalse(RegexSpecialEscape.TryDecode(escaped, out byte value));
        Assert.AreEqual(0, value);
    }
}
