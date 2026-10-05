using System.Text;

namespace Scout;

/// <summary>
/// Verifies canonical scalar intervals after insertion, merging, algebra, and case folding.
/// </summary>
[TestClass]
public sealed class RegexIntervalRegressionTests
{
    /// <summary>
    /// Verifies unordered, duplicate, adjacent, and overlapping intervals produce the same canonical set.
    /// </summary>
    /// <param name="pattern">The class expression.</param>
    [TestMethod]
    [DataRow("[zab-cd-fg-yy-z]")]
    [DataRow("[[a-z]&&[a-mn-z]]")]
    [DataRow("[[a-z]--[0-9]]")]
    [DataRow("[[a-m]~~[n-z]]")]
    public void ClassAlgebraProducesCanonicalIntervals(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        RegexAtomNode atom = Assert.IsExactInstanceOfType<RegexAtomNode>(tree.Root);
        var options = new RegexCompileOptions(false, false, false, false);
        Assert.IsTrue(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out List<RegexScalarRange> ranges));
        Assert.AreEqual(new RegexScalarRange('a', 'z'), Assert.ContainsSingle(ranges));
    }

    /// <summary>
    /// Verifies merging preserves Unicode scalar boundaries and folded equivalents.
    /// </summary>
    [TestMethod]
    public void CaseFoldingAndUtf8BoundariesRemainCanonical()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("[a-z]"u8);
        RegexAtomNode atom = Assert.IsExactInstanceOfType<RegexAtomNode>(tree.Root);
        var options = new RegexCompileOptions(true, false, false, false);
        Assert.IsTrue(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out List<RegexScalarRange> ranges));
        Assert.Contains(static range => range.Start <= 0x17F && range.End >= 0x17F, ranges);
        Assert.Contains(static range => range.Start <= 0x212A && range.End >= 0x212A, ranges);
        Assert.Contains(static range => range.Start <= 'A' && range.End >= 'Z', ranges);
        tree = RegexSyntaxParser.Parse(@"[\x{0}-\x{10FFFF}]"u8);
        atom = Assert.IsExactInstanceOfType<RegexAtomNode>(tree.Root);
        Assert.IsTrue(RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out ranges));
        Assert.AreSequenceEqual(new[] { new RegexScalarRange(0, 0xD7FF), new RegexScalarRange(0xE000, 0x10FFFF) }, ranges);
    }
}
