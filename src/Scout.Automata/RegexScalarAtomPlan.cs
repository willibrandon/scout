namespace Scout;

/// <summary>
/// Stores one immutable canonical scalar lowering shared by repeated NFA states.
/// </summary>
/// <param name="ranges">The normalized inclusive scalar ranges.</param>
internal sealed class RegexScalarAtomPlan(RegexScalarRange[] ranges)
{
    /// <summary>
    /// Gets the canonical scalar ranges.
    /// </summary>
    internal RegexScalarRange[] Ranges { get; } = ranges;

    /// <summary>
    /// Gets the equivalent byte ranges when every scalar is ASCII.
    /// </summary>
    internal byte[]? AsciiByteRanges { get; } = CreateAsciiByteRanges(ranges);

    private static byte[]? CreateAsciiByteRanges(RegexScalarRange[] ranges)
    {
        return RegexUtf8ByteCompiler.TryGetAsciiByteRanges(ranges, out byte[] byteRanges)
            ? byteRanges
            : null;
    }
}
