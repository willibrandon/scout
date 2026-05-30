using System;

namespace Scout;

/// <summary>
/// Provides byte-oriented binary input detection and conversion helpers.
/// </summary>
public static class BinaryDetection
{
    /// <summary>
    /// Detects the first NUL byte and chooses the effective binary handling mode.
    /// </summary>
    /// <param name="bytes">The input bytes.</param>
    /// <param name="textMode">Whether binary detection is disabled because input is treated as text.</param>
    /// <param name="nullData">Whether NUL bytes are configured as record terminators.</param>
    /// <param name="quitOnBinary">Whether detection should stop searching instead of converting NUL bytes.</param>
    /// <returns>The binary detection result.</returns>
    public static BinaryDetectionResult Detect(
        ReadOnlySpan<byte> bytes,
        bool textMode,
        bool nullData,
        bool quitOnBinary)
    {
        if (textMode || nullData)
        {
            return new BinaryDetectionResult(BinaryDetectionKind.None, offset: -1);
        }

        int offset = bytes.IndexOf((byte)0);
        if (offset < 0)
        {
            return new BinaryDetectionResult(BinaryDetectionKind.None, offset: -1);
        }

        return new BinaryDetectionResult(
            quitOnBinary ? BinaryDetectionKind.Quit : BinaryDetectionKind.Convert,
            offset);
    }

    /// <summary>
    /// Converts NUL bytes to line feeds for binary search processing.
    /// </summary>
    /// <param name="bytes">The source bytes.</param>
    /// <returns>A cloned byte array with each NUL replaced by <c>\n</c>.</returns>
    public static byte[] ConvertNulToLineFeed(ReadOnlySpan<byte> bytes)
    {
        byte[] convertedBytes = bytes.ToArray();
        for (int index = 0; index < convertedBytes.Length; index++)
        {
            if (convertedBytes[index] == 0)
            {
                convertedBytes[index] = (byte)'\n';
            }
        }

        return convertedBytes;
    }

    /// <summary>
    /// Returns the byte slice that should be searched for the supplied binary handling mode.
    /// </summary>
    /// <param name="bytes">The original input bytes.</param>
    /// <param name="textMode">Whether binary detection is disabled because input is treated as text.</param>
    /// <param name="nullData">Whether NUL bytes are configured as record terminators.</param>
    /// <returns>The original bytes when no conversion is needed, otherwise a converted copy.</returns>
    public static byte[] GetSearchBytes(byte[] bytes, bool textMode, bool nullData)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        BinaryDetectionResult detection = Detect(bytes, textMode, nullData, quitOnBinary: false);
        return detection.Kind == BinaryDetectionKind.Convert
            ? ConvertNulToLineFeed(bytes)
            : bytes;
    }
}
