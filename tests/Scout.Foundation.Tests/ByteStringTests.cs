
namespace Scout;

/// <summary>
/// Verifies byte-string behavior for arbitrary bytes.
/// </summary>
[TestClass]
public sealed class ByteStringTests
{
    /// <summary>
    /// Verifies copied byte strings preserve non-UTF-8 bytes.
    /// </summary>
    [TestMethod]
    public void CopyPreservesArbitraryBytes()
    {
        byte[] source = [0x66, 0x6f, 0x80, 0xff, 0x00];

        var value = ByteString.Copy(source);
        source[0] = 0x00;

        Assert.AreSequenceEqual<byte>([0x66, 0x6f, 0x80, 0xff, 0x00], value.ToArray());
    }

    /// <summary>
    /// Verifies equality is byte-for-byte and not text-based.
    /// </summary>
    [TestMethod]
    public void EqualityUsesBytes()
    {
        var left = ByteString.Copy([0xff, 0x41]);
        var right = ByteString.Copy([0xff, 0x41]);
        var different = ByteString.Copy([0xef, 0xbf, 0xbd, 0x41]);

        Assert.IsTrue(left == right);
        Assert.IsFalse(left == different);
    }

    /// <summary>
    /// Verifies span search works for single bytes and byte sequences.
    /// </summary>
    [TestMethod]
    public void SearchFindsByteSequences()
    {
        ReadOnlySpan<byte> haystack = [0x10, 0x20, 0xff, 0x00, 0x20];

        Assert.AreEqual(2, ByteSearch.IndexOf(haystack, 0xff));
        Assert.AreEqual(2, ByteSearch.IndexOf(haystack, [0xff, 0x00]));
        Assert.AreEqual(-1, ByteSearch.IndexOf(haystack, [0x00, 0xff]));
    }
}
