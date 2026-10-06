using System.Text;

namespace Scout;

/// <summary>
/// Verifies the Scout regex syntax parser.
/// </summary>
[TestClass]
public sealed class RegexSyntaxParserTests
{
    /// <summary>
    /// Verifies grouped classes, captures, alternation, repetition, and named boundaries are represented in the AST.
    /// </summary>
    [TestMethod]
    public void ParsesGroupedClassAlternationRepetitionAndBoundaries()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?P<word>[[:alpha:]]+)(?:\d{2,3}?|_\w+)\b{end}"u8);

        Assert.AreEqual(1, tree.CaptureCount);
        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(3, root.Nodes);

        RegexGroupNode capture = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[0]);
        Assert.AreEqual(RegexSyntaxKind.CapturingGroup, capture.Kind);
        Assert.AreEqual(1, capture.CaptureIndex);
        Assert.AreEqual("word", capture.CaptureName);
        RegexRepetitionNode alphaRepeat = Assert.IsExactInstanceOfType<RegexRepetitionNode>(capture.Child);
        Assert.AreEqual(1, alphaRepeat.Minimum);
        Assert.IsNull(alphaRepeat.Maximum);
        RegexAtomNode alphaClass = Assert.IsExactInstanceOfType<RegexAtomNode>(alphaRepeat.Child);
        Assert.AreEqual(RegexSyntaxKind.CharacterClass, alphaClass.Kind);
        Assert.IsTrue(alphaClass.Value.Span.SequenceEqual("[:alpha:]"u8));

        RegexGroupNode nonCapture = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[1]);
        Assert.AreEqual(RegexSyntaxKind.NonCapturingGroup, nonCapture.Kind);
        RegexAlternationNode alternation = Assert.IsExactInstanceOfType<RegexAlternationNode>(nonCapture.Child);
        Assert.HasCount(2, alternation.Alternatives);
        RegexRepetitionNode digitRepeat = Assert.IsExactInstanceOfType<RegexRepetitionNode>(alternation.Alternatives[0]);
        Assert.AreEqual(2, digitRepeat.Minimum);
        Assert.AreEqual(3, digitRepeat.Maximum);
        Assert.IsTrue(digitRepeat.Lazy);
        Assert.AreEqual(RegexSyntaxKind.DigitClass, digitRepeat.Child.Kind);

        RegexSequenceNode wordAlternative = Assert.IsExactInstanceOfType<RegexSequenceNode>(alternation.Alternatives[1]);
        Assert.HasCount(2, wordAlternative.Nodes);
        Assert.AreEqual(RegexSyntaxKind.Literal, wordAlternative.Nodes[0].Kind);
        RegexRepetitionNode wordRepeat = Assert.IsExactInstanceOfType<RegexRepetitionNode>(wordAlternative.Nodes[1]);
        Assert.AreEqual(RegexSyntaxKind.WordClass, wordRepeat.Child.Kind);
        Assert.AreEqual(1, wordRepeat.Minimum);
        Assert.IsNull(wordRepeat.Maximum);

        Assert.AreEqual(RegexSyntaxKind.WordEndBoundary, root.Nodes[2].Kind);
    }

    /// <summary>
    /// Verifies special half word-boundary assertions are parsed as zero-width atoms.
    /// </summary>
    [TestMethod]
    public void ParsesHalfWordBoundaries()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\b{start-half}foo\b{end-half}"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(5, root.Nodes);
        Assert.AreEqual(RegexSyntaxKind.WordStartHalfBoundary, root.Nodes[0].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[1].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[2].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[3].Kind);
        Assert.AreEqual(RegexSyntaxKind.WordEndHalfBoundary, root.Nodes[4].Kind);
    }

    /// <summary>
    /// Verifies absolute start and end anchors are parsed as zero-width atoms.
    /// </summary>
    [TestMethod]
    public void ParsesAbsoluteAnchors()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"\Afoo\z"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(5, root.Nodes);
        Assert.AreEqual(RegexSyntaxKind.AbsoluteStartAnchor, root.Nodes[0].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[1].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[2].Kind);
        Assert.AreEqual(RegexSyntaxKind.Literal, root.Nodes[3].Kind);
        Assert.AreEqual(RegexSyntaxKind.AbsoluteEndAnchor, root.Nodes[4].Kind);
    }

    /// <summary>
    /// Verifies scoped and unscoped inline flags and byte escapes are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesInlineFlagsAndByteEscapes()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?ix-s:f\x6fo)(?-i)\u{21}"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(3, root.Nodes);
        RegexGroupNode scopedFlags = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[0]);
        Assert.AreEqual("ix", scopedFlags.EnabledFlags);
        Assert.AreEqual("s", scopedFlags.DisabledFlags);
        RegexSequenceNode scopedBody = Assert.IsExactInstanceOfType<RegexSequenceNode>(scopedFlags.Child);
        Assert.HasCount(3, scopedBody.Nodes);
        RegexAtomNode hexLiteral = Assert.IsExactInstanceOfType<RegexAtomNode>(scopedBody.Nodes[1]);
        Assert.AreEqual((byte)'o', hexLiteral.Value.Span[0]);

        RegexInlineFlagsNode inlineFlags = Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(root.Nodes[1]);
        Assert.AreEqual(string.Empty, inlineFlags.EnabledFlags);
        Assert.AreEqual("i", inlineFlags.DisabledFlags);
        RegexAtomNode scalarLiteral = Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[2]);
        Assert.AreEqual((byte)'!', scalarLiteral.Value.Span[0]);
    }

    /// <summary>
    /// Verifies unscoped inline flags are inherited by later alternatives in the same group.
    /// </summary>
    [TestMethod]
    public void PropagatesUnscopedInlineFlagsAcrossAlternatives()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("a(?i)b|c(?-i)d|e"u8);

        RegexAlternationNode root = Assert.IsExactInstanceOfType<RegexAlternationNode>(tree.Root);
        RegexSequenceNode second = Assert.IsExactInstanceOfType<RegexSequenceNode>(root.Alternatives[1]);
        RegexInlineFlagsNode inheritedEnable = Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(second.Nodes[0]);
        Assert.AreEqual("i", inheritedEnable.EnabledFlags);
        Assert.IsEmpty(inheritedEnable.DisabledFlags);

        RegexSequenceNode third = Assert.IsExactInstanceOfType<RegexSequenceNode>(root.Alternatives[2]);
        TestAssert.Collection(
                    third.Nodes,
                    node => Assert.AreEqual("i", Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(node).EnabledFlags),
                    node => Assert.AreEqual("i", Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(node).DisabledFlags),
                    node => Assert.AreEqual((byte)'e', Assert.IsExactInstanceOfType<RegexAtomNode>(node).Value.Span[0]));
    }

    /// <summary>
    /// Verifies extended-mode whitespace and comments are parse-time syntax.
    /// </summary>
    [TestMethod]
    public void ParsesExtendedModeWhitespaceAndComments()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?x) a # comment\n b\\ c"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(5, root.Nodes);
        Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(root.Nodes[0]);
        Assert.AreEqual((byte)'a', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)'b', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[2]).Value.Span[0]);
        Assert.AreEqual((byte)' ', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[3]).Value.Span[0]);
        Assert.AreEqual((byte)'c', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[4]).Value.Span[0]);
    }

    /// <summary>
    /// Verifies an unscoped extended-mode flag is confined to its enclosing alternation branch.
    /// </summary>
    [TestMethod]
    public void ConfinesUnscopedExtendedModeToEnclosingBranchGroup()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?:(?x)a b)|(?:c d)"u8);

        RegexAlternationNode root = Assert.IsExactInstanceOfType<RegexAlternationNode>(tree.Root);
        RegexGroupNode extendedBranch = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Alternatives[0]);
        RegexSequenceNode extendedSequence = Assert.IsExactInstanceOfType<RegexSequenceNode>(extendedBranch.Child);
        Assert.HasCount(3, extendedSequence.Nodes);
        Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(extendedSequence.Nodes[0]);
        Assert.AreEqual((byte)'a', Assert.IsExactInstanceOfType<RegexAtomNode>(extendedSequence.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)'b', Assert.IsExactInstanceOfType<RegexAtomNode>(extendedSequence.Nodes[2]).Value.Span[0]);

        RegexGroupNode literalBranch = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Alternatives[1]);
        RegexSequenceNode literalSequence = Assert.IsExactInstanceOfType<RegexSequenceNode>(literalBranch.Child);
        Assert.HasCount(3, literalSequence.Nodes);
        Assert.AreEqual((byte)'c', Assert.IsExactInstanceOfType<RegexAtomNode>(literalSequence.Nodes[0]).Value.Span[0]);
        Assert.AreEqual((byte)' ', Assert.IsExactInstanceOfType<RegexAtomNode>(literalSequence.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)'d', Assert.IsExactInstanceOfType<RegexAtomNode>(literalSequence.Nodes[2]).Value.Span[0]);
    }

    /// <summary>
    /// Verifies nested unscoped extended-mode changes restore the mode of each enclosing group.
    /// </summary>
    [TestMethod]
    public void RestoresExtendedModeAcrossNestedGroups()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?x:(?:(?-x)a b)c d)e f"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(4, root.Nodes);
        RegexGroupNode outerGroup = Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[0]);
        RegexSequenceNode outerSequence = Assert.IsExactInstanceOfType<RegexSequenceNode>(outerGroup.Child);
        Assert.HasCount(3, outerSequence.Nodes);

        RegexGroupNode innerGroup = Assert.IsExactInstanceOfType<RegexGroupNode>(outerSequence.Nodes[0]);
        RegexSequenceNode innerSequence = Assert.IsExactInstanceOfType<RegexSequenceNode>(innerGroup.Child);
        Assert.HasCount(4, innerSequence.Nodes);
        Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(innerSequence.Nodes[0]);
        Assert.AreEqual((byte)' ', Assert.IsExactInstanceOfType<RegexAtomNode>(innerSequence.Nodes[2]).Value.Span[0]);

        Assert.AreEqual((byte)'c', Assert.IsExactInstanceOfType<RegexAtomNode>(outerSequence.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)'d', Assert.IsExactInstanceOfType<RegexAtomNode>(outerSequence.Nodes[2]).Value.Span[0]);
        Assert.AreEqual((byte)'e', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)' ', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[2]).Value.Span[0]);
        Assert.AreEqual((byte)'f', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[3]).Value.Span[0]);
    }

    /// <summary>
    /// Verifies an unscoped extended-mode flag is confined by capturing group boundaries.
    /// </summary>
    [TestMethod]
    [DataRow("((?x)a b)c d")]
    [DataRow("(?<name>(?x)a b)c d")]
    public void ConfinesUnscopedExtendedModeToCapturingGroup(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.ASCII.GetBytes(pattern));

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(4, root.Nodes);
        Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[0]);
        Assert.AreEqual((byte)'c', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[1]).Value.Span[0]);
        Assert.AreEqual((byte)' ', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[2]).Value.Span[0]);
        Assert.AreEqual((byte)'d', Assert.IsExactInstanceOfType<RegexAtomNode>(root.Nodes[3]).Value.Span[0]);
    }

    /// <summary>
    /// Verifies duplicate named captures are rejected across an entire parsed expression.
    /// </summary>
    [TestMethod]
    [DataRow("(?P<word>a)(?<word>b)")]
    [DataRow("(?<word>a)|(?P<word>b)")]
    [DataRow("(?<word>a(?<word>b))")]
    public void RejectsDuplicateCaptureGroupNames(string pattern)
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(
            () => RegexSyntaxParser.Parse(Encoding.ASCII.GetBytes(pattern)));

        Assert.Contains("duplicate capture group name", exception.Message, StringComparison.Ordinal);
        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies capture group names remain case-sensitive.
    /// </summary>
    [TestMethod]
    public void AllowsCaptureGroupNamesThatDifferByCase()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?<Word>a)(?<word>b)"u8);

        Assert.AreEqual(2, tree.CaptureCount);
        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.AreEqual("Word", Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[0]).CaptureName);
        Assert.AreEqual("word", Assert.IsExactInstanceOfType<RegexGroupNode>(root.Nodes[1]).CaptureName);
    }

    /// <summary>
    /// Verifies syntax errors are reported with a byte offset.
    /// </summary>
    [TestMethod]
    [DataRow("(")]
    [DataRow("[[:alpha:]")]
    [DataRow("(?P<1bad>a)")]
    [DataRow("*")]
    [DataRow("+")]
    [DataRow("?")]
    [DataRow("(*)")]
    [DataRow("(?)")]
    [DataRow("(?:?)")]
    public void ReportsSyntaxErrors(string pattern)
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(() => RegexSyntaxParser.Parse(Encoding.ASCII.GetBytes(pattern)));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies chained quantifiers remain nested repetition syntax.
    /// </summary>
    [TestMethod]
    public void ParsesChainedQuantifiersAsNestedRepetitions()
    {
        RegexRepetitionNode outer = Assert.IsExactInstanceOfType<RegexRepetitionNode>(RegexSyntaxParser.Parse("t{1,2}+"u8).Root);
        Assert.AreEqual(1, outer.Minimum);
        Assert.IsNull(outer.Maximum);

        RegexRepetitionNode inner = Assert.IsExactInstanceOfType<RegexRepetitionNode>(outer.Child);
        Assert.AreEqual(1, inner.Minimum);
        Assert.AreEqual(2, inner.Maximum);
        Assert.AreEqual((byte)'t', Assert.IsExactInstanceOfType<RegexAtomNode>(inner.Child).Value.Span[0]);
    }

    /// <summary>
    /// Verifies unknown alphanumeric escapes, backreferences, and invalid scalars are rejected.
    /// </summary>
    [TestMethod]
    [DataRow(@"\q")]
    [DataRow(@"\1")]
    [DataRow(@"\K")]
    [DataRow(@"\R")]
    [DataRow(@"\X")]
    [DataRow(@"\x{}")]
    [DataRow(@"\uD800")]
    [DataRow(@"\U00110000")]
    [DataRow(@"\p{NotAUnicodeProperty}")]
    public void RejectsUnsupportedEscapesAndInvalidScalars(string pattern)
    {
        FormatException exception = Assert.ThrowsExactly<FormatException>(
            () => RegexSyntaxParser.Parse(Encoding.ASCII.GetBytes(pattern)));

        Assert.Contains("byte offset", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies extended mode ignores syntax whitespace in escapes and repetition counts.
    /// </summary>
    [TestMethod]
    public void ParsesExtendedWhitespaceWithinEscapesAndRepetitions()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?x)\\x 4 1 { 1 , 2 } \\p{ Latin }"u8);

        RegexSequenceNode root = Assert.IsExactInstanceOfType<RegexSequenceNode>(tree.Root);
        Assert.HasCount(3, root.Nodes);
        Assert.IsExactInstanceOfType<RegexInlineFlagsNode>(root.Nodes[0]);
        RegexRepetitionNode repetition = Assert.IsExactInstanceOfType<RegexRepetitionNode>(root.Nodes[1]);
        Assert.AreEqual(1, repetition.Minimum);
        Assert.AreEqual(2, repetition.Maximum);
        Assert.AreEqual(RegexSyntaxKind.UnicodePropertyClass, root.Nodes[2].Kind);
    }

}
