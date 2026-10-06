
namespace Scout;

/// <summary>
/// Verifies binary input detection and conversion helpers.
/// </summary>
[TestClass]
public sealed class BinaryDetectionTests
{
    /// <summary>
    /// Verifies binary detection reports no binary data for text and NUL-data modes.
    /// </summary>
    [TestMethod]
    public void DetectHonorsTextAndNullDataModes()
    {
        Assert.AreEqual(
            new BinaryDetectionResult(BinaryDetectionKind.None, -1),
            BinaryDetection.Detect("a\0b"u8, textMode: true, nullData: false, quitOnBinary: false));
        Assert.AreEqual(
            new BinaryDetectionResult(BinaryDetectionKind.None, -1),
            BinaryDetection.Detect("a\0b"u8, textMode: false, nullData: true, quitOnBinary: false));
    }

    /// <summary>
    /// Verifies binary detection distinguishes conversion from quit mode.
    /// </summary>
    [TestMethod]
    public void DetectReportsConvertOrQuitAtFirstNul()
    {
        Assert.AreEqual(
            new BinaryDetectionResult(BinaryDetectionKind.Convert, 1),
            BinaryDetection.Detect("a\0b\0"u8, textMode: false, nullData: false, quitOnBinary: false));
        Assert.AreEqual(
            new BinaryDetectionResult(BinaryDetectionKind.Quit, 1),
            BinaryDetection.Detect("a\0b\0"u8, textMode: false, nullData: false, quitOnBinary: true));
    }

    /// <summary>
    /// Verifies binary conversion maps all NUL bytes to line feeds without mutating the source.
    /// </summary>
    [TestMethod]
    public void ConvertNulToLineFeedClonesAndConverts()
    {
        byte[] bytes = [(byte)'a', 0, (byte)'b', 0];

        byte[] converted = BinaryDetection.ConvertNulToLineFeed(bytes);

        Assert.AreSequenceEqual("a\nb\n"u8.ToArray(), converted);
        Assert.AreSequenceEqual<byte>([(byte)'a', 0, (byte)'b', 0], bytes);
    }

    /// <summary>
    /// Verifies search-byte selection returns the original instance when no conversion is required.
    /// </summary>
    [TestMethod]
    public void GetSearchBytesAvoidsCopiesWhenConversionIsDisabledOrUnneeded()
    {
        byte[] text = "abc"u8.ToArray();
        byte[] binary = [(byte)'a', 0, (byte)'b'];

        Assert.AreSame(text, BinaryDetection.GetSearchBytes(text, textMode: false, nullData: false));
        Assert.AreSame(binary, BinaryDetection.GetSearchBytes(binary, textMode: true, nullData: false));
        Assert.AreSame(binary, BinaryDetection.GetSearchBytes(binary, textMode: false, nullData: true));
        Assert.AreNotSame(binary, BinaryDetection.GetSearchBytes(binary, textMode: false, nullData: false));
    }

    /// <summary>
    /// Verifies invalid detection result offsets are rejected.
    /// </summary>
    [TestMethod]
    public void BinaryDetectionResultRejectsInvalidOffset()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BinaryDetectionResult(BinaryDetectionKind.None, -2));
    }
}
