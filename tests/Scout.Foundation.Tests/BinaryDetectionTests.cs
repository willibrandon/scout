
namespace Scout;

/// <summary>
/// Verifies binary input detection and conversion helpers.
/// </summary>
public sealed class BinaryDetectionTests
{
    /// <summary>
    /// Verifies binary detection reports no binary data for text and NUL-data modes.
    /// </summary>
    [Fact]
    public void DetectHonorsTextAndNullDataModes()
    {
        Assert.Equal(
            new BinaryDetectionResult(BinaryDetectionKind.None, -1),
            BinaryDetection.Detect("a\0b"u8, textMode: true, nullData: false, quitOnBinary: false));
        Assert.Equal(
            new BinaryDetectionResult(BinaryDetectionKind.None, -1),
            BinaryDetection.Detect("a\0b"u8, textMode: false, nullData: true, quitOnBinary: false));
    }

    /// <summary>
    /// Verifies binary detection distinguishes conversion from quit mode.
    /// </summary>
    [Fact]
    public void DetectReportsConvertOrQuitAtFirstNul()
    {
        Assert.Equal(
            new BinaryDetectionResult(BinaryDetectionKind.Convert, 1),
            BinaryDetection.Detect("a\0b\0"u8, textMode: false, nullData: false, quitOnBinary: false));
        Assert.Equal(
            new BinaryDetectionResult(BinaryDetectionKind.Quit, 1),
            BinaryDetection.Detect("a\0b\0"u8, textMode: false, nullData: false, quitOnBinary: true));
    }

    /// <summary>
    /// Verifies binary conversion maps all NUL bytes to line feeds without mutating the source.
    /// </summary>
    [Fact]
    public void ConvertNulToLineFeedClonesAndConverts()
    {
        byte[] bytes = [(byte)'a', 0, (byte)'b', 0];

        byte[] converted = BinaryDetection.ConvertNulToLineFeed(bytes);

        Assert.Equal("a\nb\n"u8.ToArray(), converted);
        Assert.Equal([(byte)'a', 0, (byte)'b', 0], bytes);
    }

    /// <summary>
    /// Verifies search-byte selection returns the original instance when no conversion is required.
    /// </summary>
    [Fact]
    public void GetSearchBytesAvoidsCopiesWhenConversionIsDisabledOrUnneeded()
    {
        byte[] text = "abc"u8.ToArray();
        byte[] binary = [(byte)'a', 0, (byte)'b'];

        Assert.Same(text, BinaryDetection.GetSearchBytes(text, textMode: false, nullData: false));
        Assert.Same(binary, BinaryDetection.GetSearchBytes(binary, textMode: true, nullData: false));
        Assert.Same(binary, BinaryDetection.GetSearchBytes(binary, textMode: false, nullData: true));
        Assert.NotSame(binary, BinaryDetection.GetSearchBytes(binary, textMode: false, nullData: false));
    }

    /// <summary>
    /// Verifies invalid detection result offsets are rejected.
    /// </summary>
    [Fact]
    public void BinaryDetectionResultRejectsInvalidOffset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryDetectionResult(BinaryDetectionKind.None, -2));
    }
}
