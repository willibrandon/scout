using System.Text;

namespace Scout;

/// <summary>
/// Verifies canonical scalar intervals after insertion, merging, algebra, and case folding.
/// </summary>
public sealed class RegexIntervalRegressionTests
{
    /// <summary>
    /// Verifies unordered, duplicate, adjacent, and overlapping intervals produce the same canonical set.
    /// </summary>
    /// <param name="pattern">The class expression.</param>
    [Theory]
    [InlineData("[zab-cd-fg-yy-z]")]
    [InlineData("[[a-z]&&[a-mn-z]]")]
    [InlineData("[[a-z]--[0-9]]")]
    [InlineData("[[a-m]~~[n-z]]")]
    public void ClassAlgebraProducesCanonicalIntervals(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        RegexAtomNode atom = Assert.IsType<RegexAtomNode>(tree.Root);
        var options = new RegexCompileOptions(false, false, false, false);
        Assert.True(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out List<RegexScalarRange> ranges));
        Assert.Equal(new RegexScalarRange('a', 'z'), Assert.Single(ranges));
    }

    /// <summary>
    /// Verifies merging preserves Unicode scalar boundaries and folded equivalents.
    /// </summary>
    [Fact]
    public void CaseFoldingAndUtf8BoundariesRemainCanonical()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a-z]"u8);
        RegexAtomNode atom = Assert.IsType<RegexAtomNode>(tree.Root);
        var options = new RegexCompileOptions(true, false, false, false);
        Assert.True(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out List<RegexScalarRange> ranges));
        Assert.Contains(ranges, static range => range.Start <= 0x17F && range.End >= 0x17F);
        Assert.Contains(ranges, static range => range.Start <= 0x212A && range.End >= 0x212A);
        Assert.Contains(ranges, static range => range.Start <= 'A' && range.End >= 'Z');
        tree = RegexSyntaxParser.Parse(@"[\x{0}-\x{10FFFF}]"u8);
        atom = Assert.IsType<RegexAtomNode>(tree.Root);
        Assert.True(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out ranges));
        Assert.Equal(new[] { new RegexScalarRange(0, 0xD7FF), new RegexScalarRange(0xE000, 0x10FFFF) }, ranges);
    }
}
