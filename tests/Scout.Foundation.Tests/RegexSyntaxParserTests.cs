using System.Text;

namespace Scout;

/// <summary>
/// Verifies the Scout regex syntax parser.
/// </summary>
public sealed class RegexSyntaxParserTests
{
    /// <summary>
    /// Verifies grouped classes, captures, alternation, repetition, and named boundaries are represented in the AST.
    /// </summary>
    [Fact]
    public void ParsesGroupedClassAlternationRepetitionAndBoundaries()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?P<word>[[:alpha:]]+)(?:\d{2,3}?|_\w+)\b{end}"u8);

        Assert.Equal(1, tree.CaptureCount);
        RegexSequenceNode root = Assert.IsType<RegexSequenceNode>(tree.Root);
        Assert.Equal(3, root.Nodes.Count);

        RegexGroupNode capture = Assert.IsType<RegexGroupNode>(root.Nodes[0]);
        Assert.Equal(RegexSyntaxKind.CapturingGroup, capture.Kind);
        Assert.Equal(1, capture.CaptureIndex);
        Assert.Equal("word", capture.CaptureName);
        RegexRepetitionNode alphaRepeat = Assert.IsType<RegexRepetitionNode>(capture.Child);
        Assert.Equal(1, alphaRepeat.Minimum);
        Assert.Null(alphaRepeat.Maximum);
        RegexAtomNode alphaClass = Assert.IsType<RegexAtomNode>(alphaRepeat.Child);
        Assert.Equal(RegexSyntaxKind.CharacterClass, alphaClass.Kind);
        Assert.True(alphaClass.Value.Span.SequenceEqual("[:alpha:]"u8));

        RegexGroupNode nonCapture = Assert.IsType<RegexGroupNode>(root.Nodes[1]);
        Assert.Equal(RegexSyntaxKind.NonCapturingGroup, nonCapture.Kind);
        RegexAlternationNode alternation = Assert.IsType<RegexAlternationNode>(nonCapture.Child);
        Assert.Equal(2, alternation.Alternatives.Count);
        RegexRepetitionNode digitRepeat = Assert.IsType<RegexRepetitionNode>(alternation.Alternatives[0]);
        Assert.Equal(2, digitRepeat.Minimum);
        Assert.Equal(3, digitRepeat.Maximum);
        Assert.True(digitRepeat.Lazy);
        Assert.Equal(RegexSyntaxKind.DigitClass, digitRepeat.Child.Kind);

        RegexSequenceNode wordAlternative = Assert.IsType<RegexSequenceNode>(alternation.Alternatives[1]);
        Assert.Equal(2, wordAlternative.Nodes.Count);
        Assert.Equal(RegexSyntaxKind.Literal, wordAlternative.Nodes[0].Kind);
        RegexRepetitionNode wordRepeat = Assert.IsType<RegexRepetitionNode>(wordAlternative.Nodes[1]);
        Assert.Equal(RegexSyntaxKind.WordClass, wordRepeat.Child.Kind);
        Assert.Equal(1, wordRepeat.Minimum);
        Assert.Null(wordRepeat.Maximum);

        Assert.Equal(RegexSyntaxKind.WordEndBoundary, root.Nodes[2].Kind);
    }

    /// <summary>
    /// Verifies special half word-boundary assertions are parsed as zero-width atoms.
    /// </summary>
    [Fact]
    public void ParsesHalfWordBoundaries()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\b{start-half}foo\b{end-half}"u8);

        RegexSequenceNode root = Assert.IsType<RegexSequenceNode>(tree.Root);
        Assert.Equal(5, root.Nodes.Count);
        Assert.Equal(RegexSyntaxKind.WordStartHalfBoundary, root.Nodes[0].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[1].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[2].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[3].Kind);
        Assert.Equal(RegexSyntaxKind.WordEndHalfBoundary, root.Nodes[4].Kind);
    }

    /// <summary>
    /// Verifies absolute start and end anchors are parsed as zero-width atoms.
    /// </summary>
    [Fact]
    public void ParsesAbsoluteAnchors()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\Afoo\z"u8);

        RegexSequenceNode root = Assert.IsType<RegexSequenceNode>(tree.Root);
        Assert.Equal(5, root.Nodes.Count);
        Assert.Equal(RegexSyntaxKind.AbsoluteStartAnchor, root.Nodes[0].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[1].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[2].Kind);
        Assert.Equal(RegexSyntaxKind.Literal, root.Nodes[3].Kind);
        Assert.Equal(RegexSyntaxKind.AbsoluteEndAnchor, root.Nodes[4].Kind);
    }

    /// <summary>
    /// Verifies scoped and unscoped inline flags and byte escapes are parsed.
    /// </summary>
    [Fact]
    public void ParsesInlineFlagsAndByteEscapes()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?ix-s:f\x6fo)(?-i)\u{21}"u8);

        RegexSequenceNode root = Assert.IsType<RegexSequenceNode>(tree.Root);
        Assert.Equal(3, root.Nodes.Count);
        RegexGroupNode scopedFlags = Assert.IsType<RegexGroupNode>(root.Nodes[0]);
        Assert.Equal("ix", scopedFlags.EnabledFlags);
        Assert.Equal("s", scopedFlags.DisabledFlags);
        RegexSequenceNode scopedBody = Assert.IsType<RegexSequenceNode>(scopedFlags.Child);
        Assert.Equal(3, scopedBody.Nodes.Count);
        RegexAtomNode hexLiteral = Assert.IsType<RegexAtomNode>(scopedBody.Nodes[1]);
        Assert.Equal((byte)'o', hexLiteral.Value.Span[0]);

        RegexInlineFlagsNode inlineFlags = Assert.IsType<RegexInlineFlagsNode>(root.Nodes[1]);
        Assert.Equal(string.Empty, inlineFlags.EnabledFlags);
        Assert.Equal("i", inlineFlags.DisabledFlags);
        RegexAtomNode scalarLiteral = Assert.IsType<RegexAtomNode>(root.Nodes[2]);
        Assert.Equal((byte)'!', scalarLiteral.Value.Span[0]);
    }

    /// <summary>
    /// Verifies extended-mode whitespace and comments are parse-time syntax.
    /// </summary>
    [Fact]
    public void ParsesExtendedModeWhitespaceAndComments()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?x) a # comment\n b\\ c"u8);

        RegexSequenceNode root = Assert.IsType<RegexSequenceNode>(tree.Root);
        Assert.Equal(5, root.Nodes.Count);
        Assert.IsType<RegexInlineFlagsNode>(root.Nodes[0]);
        Assert.Equal((byte)'a', Assert.IsType<RegexAtomNode>(root.Nodes[1]).Value.Span[0]);
        Assert.Equal((byte)'b', Assert.IsType<RegexAtomNode>(root.Nodes[2]).Value.Span[0]);
        Assert.Equal((byte)' ', Assert.IsType<RegexAtomNode>(root.Nodes[3]).Value.Span[0]);
        Assert.Equal((byte)'c', Assert.IsType<RegexAtomNode>(root.Nodes[4]).Value.Span[0]);
    }

    /// <summary>
    /// Verifies syntax errors are reported with a byte offset.
    /// </summary>
    [Theory]
    [InlineData("(")]
    [InlineData("[[:alpha:]")]
    [InlineData("(?P<1bad>a)")]
    [InlineData(@"\u{100}")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("?")]
    [InlineData("(*)")]
    [InlineData("(?)")]
    [InlineData("(?:?)")]
    public void ReportsSyntaxErrors(string pattern)
    {
        FormatException exception = Assert.Throws<FormatException>(() => RegexSyntaxParser.Parse(Encoding.ASCII.GetBytes(pattern)));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
    }
}
