using System.Text;

namespace Scout;

/// <summary>
/// Verifies that standard-search statistics are produced by the output traversal.
/// </summary>
[TestClass]
public sealed class StandardSearchByteOperationsTests
{
    /// <summary>
    /// Verifies line, match, summary, suppression, and replay modes satisfy the one-traversal invariant.
    /// </summary>
    [TestMethod]
    public void StatsModesUseOneAuthoritativeTraversal()
    {
        AssertSingleTraversal();
        AssertSingleTraversal(onlyMatching: true);
        AssertSingleTraversal(replacement: "$0!"u8.ToArray());
        AssertSingleTraversal(color: true);
        AssertSingleTraversal(invertMatch: true, expectedMatchedLines: 1, expectedMatches: 0);
        AssertSingleTraversal(searchMode: CliSearchMode.Count);
        AssertSingleTraversal(searchMode: CliSearchMode.CountMatches);
        AssertSingleTraversal(searchMode: CliSearchMode.FilesWithMatches);
        AssertSingleTraversal(searchMode: CliSearchMode.FilesWithoutMatch);
        AssertSingleTraversal(quiet: true);
        AssertSingleTraversal(beforeContext: 1);
        AssertSingleTraversal(stopOnNonmatch: true, expectedMatchedLines: 1, expectedMatches: 2);
        AssertSingleTraversal(
            contents: "alpha\nbeta\nmiss\n",
            expression: "alpha\\nbeta",
            multiline: true,
            expectedMatchedLines: 2,
            expectedMatches: 1);

        byte[] binaryBytes = new byte[70_007];
        Array.Fill(binaryBytes, (byte)'x');
        "alpha\n"u8.CopyTo(binaryBytes);
        binaryBytes[70_000] = 0;
        "alpha\n"u8.CopyTo(binaryBytes.AsSpan(70_001));
        AssertSingleTraversal(
            inputBytes: binaryBytes,
            textMode: false,
            expectedMatchedLines: 2,
            expectedMatches: 2);
        AssertSingleTraversal(
            inputBytes: binaryBytes,
            textMode: false,
            memoryMapped: true,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 2,
            expectedMatches: 2);
    }

    /// <summary>
    /// Verifies mapped after-context searches retain matches on either side of an early binary byte.
    /// </summary>
    [TestMethod]
    [DataRow("a\0b\nneedle\n", 1UL)]
    [DataRow("needle\0binary\nafter\n", 6UL)]
    public void MmapAfterContextPreservesMatchesAcrossBinaryOffset(
        string contents,
        ulong binaryOffset)
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: Encoding.UTF8.GetBytes(contents),
            textMode: false,
            memoryMapped: true,
            afterContext: 1,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(matched);
        Assert.AreEqual(
            $"binary file matches (found \"\\0\" byte around offset {binaryOffset})\n",
            Encoding.UTF8.GetString(output));
        Assert.AreEqual(binaryOffset, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies max-count cannot expand a searched extent already bounded by an mmap binary offset.
    /// </summary>
    [TestMethod]
    public void MmapMaxCountKeepsBinaryBoundedStatsExtent()
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: "a\0b\nneedle\n"u8.ToArray(),
            textMode: false,
            memoryMapped: true,
            maxCount: 1,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(matched);
        Assert.AreEqual(
            "binary file matches (found \"\\0\" byte around offset 1)\n",
            Encoding.UTF8.GetString(output));
        Assert.AreEqual(1UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies late binary bytes are detected in every physical line emitted by mapped replay.
    /// </summary>
    [TestMethod]
    public void MmapReplayDetectsLateBinaryBytesInIncludedLines()
    {
        byte[] afterContext = CreateLateBinaryInput(
            includeTrailingMatch: false);
        (bool afterMatched, byte[] afterOutput, _) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: afterContext,
            textMode: false,
            memoryMapped: true,
            afterContext: 1,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 1,
            expectedMatches: 1);
        Assert.IsTrue(afterMatched);
        Assert.AreEqual(
            "1:needle\nbinary file matches (found \"\\0\" byte around offset 69000)\n",
            Encoding.UTF8.GetString(afterOutput));

        byte[] beforeContext = CreateLateBinaryInput(
            includeTrailingMatch: true);
        (bool beforeMatched, byte[] beforeOutput, _) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: beforeContext,
            textMode: false,
            memoryMapped: true,
            beforeContext: 1,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 2,
            expectedMatches: 2);
        Assert.IsTrue(beforeMatched);
        Assert.AreEqual(
            "1:needle\nbinary file matches " +
            "(found \"\\0\" byte around offset 69000)\n",
            Encoding.UTF8.GetString(beforeOutput));

        (bool passthruMatched, byte[] passthruOutput, _) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: afterContext,
            textMode: false,
            memoryMapped: true,
            passthru: true,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 1,
            expectedMatches: 1);
        Assert.IsTrue(passthruMatched);
        Assert.AreEqual(
            "1:needle\nbinary file matches " +
            "(found \"\\0\" byte around offset 69000)\n",
            Encoding.UTF8.GetString(passthruOutput));
    }

    /// <summary>
    /// Verifies max-count excludes context belonging only to later selected lines from mapped binary detection.
    /// </summary>
    [TestMethod]
    public void MmapReplayBinaryDetectionUsesMaxCountInclusionMap()
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: CreateLateBinaryInput(includeTrailingMatch: true),
            textMode: false,
            memoryMapped: true,
            beforeContext: 1,
            maxCount: 1,
            binaryDetectionScope: StandardBinaryDetectionScope.SelectedLines,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(matched);
        Assert.AreEqual("1:needle\n", Encoding.UTF8.GetString(output));
        Assert.AreEqual(7UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies buffered context search does not treat the fragment before an early NUL as a complete line.
    /// </summary>
    [TestMethod]
    public void BufferedContextSearchRejectsTruncatedBinaryLine()
    {
        (bool matched, byte[] output, _) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: "before\nneedle\0binary\nafter\n"u8.ToArray(),
            textMode: false,
            beforeContext: 1,
            afterContext: 1,
            expectedMatchedLines: 0,
            expectedMatches: 0);

        Assert.IsFalse(matched);
        Assert.IsEmpty(output);
    }

    /// <summary>
    /// Verifies buffered context search replays complete selected lines before a binary byte in a later buffer.
    /// </summary>
    [TestMethod]
    public void BufferedContextSearchReplaysCompletePrefixBeforeLateBinaryByte()
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: CreateLateBinaryInput(includeTrailingMatch: false),
            textMode: false,
            afterContext: 1,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(matched);
        Assert.AreEqual(
            "1:needle\nbinary file matches (found \"\\0\" byte around offset 69000)\n",
            Encoding.UTF8.GetString(output));
        Assert.AreEqual(7UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies binary notification suppresses the same early match and context callbacks as ripgrep's buffered reader.
    /// </summary>
    [TestMethod]
    public void BufferedBinaryReplayMatchesRipgrepBeforeFirstReportedMatch()
    {
        byte[] bytes = "binary\0data\nneedle\n"u8.ToArray();
        const string BinaryMessage =
            "binary file matches (found \"\\0\" byte around offset 6)\n";

        (bool plainMatched, byte[] plainOutput, SearchStats plainStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(plainMatched);
        Assert.AreEqual(BinaryMessage, Encoding.UTF8.GetString(plainOutput));
        Assert.AreEqual(19UL, plainStats.BytesSearched);

        (bool afterMatched, byte[] afterOutput, SearchStats afterStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                afterContext: 1,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(afterMatched);
        Assert.AreEqual(BinaryMessage, Encoding.UTF8.GetString(afterOutput));
        Assert.AreEqual(19UL, afterStats.BytesSearched);

        (bool beforeMatched, byte[] beforeOutput, SearchStats beforeStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                expectedMatchedLines: 0,
                expectedMatches: 0);
        Assert.IsFalse(beforeMatched);
        Assert.IsEmpty(beforeOutput);
        Assert.AreEqual(0UL, beforeStats.BytesSearched);

        (bool contextMatched, byte[] contextOutput, SearchStats contextStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                afterContext: 1,
                expectedMatchedLines: 0,
                expectedMatches: 0);
        Assert.IsFalse(contextMatched);
        Assert.IsEmpty(contextOutput);
        Assert.AreEqual(0UL, contextStats.BytesSearched);

        (bool passthruMatched, byte[] passthruOutput, SearchStats passthruStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                passthru: true,
                expectedMatchedLines: 0,
                expectedMatches: 0);
        Assert.IsFalse(passthruMatched);
        Assert.IsEmpty(passthruOutput);
        Assert.AreEqual(7UL, passthruStats.BytesSearched);
    }

    /// <summary>
    /// Verifies buffered binary replay preserves ripgrep's callback order after an earlier printed match.
    /// </summary>
    [TestMethod]
    public void BufferedBinaryReplayMatchesRipgrepAfterReportedMatch()
    {
        byte[] bytes = CreateLateBinaryInput(includeTrailingMatch: true);
        const string MatchLine = "1:needle\n";
        const string BinaryMessage =
            "binary file matches (found \"\\0\" byte around offset 69000)\n";

        (bool beforeMatched, byte[] beforeOutput, SearchStats beforeStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(beforeMatched);
        Assert.AreEqual(
            MatchLine + "--\n" + BinaryMessage,
            Encoding.UTF8.GetString(beforeOutput));
        Assert.AreEqual(7UL, beforeStats.BytesSearched);

        (bool widerBeforeMatched, byte[] widerBeforeOutput, SearchStats widerBeforeStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 2,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(widerBeforeMatched);
        Assert.AreEqual(
            MatchLine + BinaryMessage,
            Encoding.UTF8.GetString(widerBeforeOutput));
        Assert.AreEqual(7UL, widerBeforeStats.BytesSearched);

        (bool contextMatched, byte[] contextOutput, SearchStats contextStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                afterContext: 1,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(contextMatched);
        Assert.AreEqual(
            MatchLine + BinaryMessage,
            Encoding.UTF8.GetString(contextOutput));
        Assert.AreEqual(7UL, contextStats.BytesSearched);

        (bool passthruMatched, byte[] passthruOutput, SearchStats passthruStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                passthru: true,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(passthruMatched);
        Assert.AreEqual(
            MatchLine + BinaryMessage,
            Encoding.UTF8.GetString(passthruOutput));
        Assert.AreEqual(69_001UL, passthruStats.BytesSearched);

        (bool beforeMaxMatched, byte[] beforeMaxOutput, SearchStats beforeMaxStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                maxCount: 1,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(beforeMaxMatched);
        Assert.AreEqual(MatchLine, Encoding.UTF8.GetString(beforeMaxOutput));
        Assert.AreEqual(7UL, beforeMaxStats.BytesSearched);

        (bool afterMaxMatched, byte[] afterMaxOutput, SearchStats afterMaxStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                afterContext: 1,
                maxCount: 1,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(afterMaxMatched);
        Assert.AreEqual(
            MatchLine + BinaryMessage,
            Encoding.UTF8.GetString(afterMaxOutput));
        Assert.AreEqual(7UL, afterMaxStats.BytesSearched);

        (bool stoppedMatched, byte[] stoppedOutput, SearchStats stoppedStats) =
            AssertSingleTraversal(
                expression: "needle",
                inputBytes: bytes,
                textMode: false,
                beforeContext: 1,
                stopOnNonmatch: true,
                expectedMatchedLines: 1,
                expectedMatches: 1);
        Assert.IsTrue(stoppedMatched);
        Assert.AreEqual(
            MatchLine + BinaryMessage,
            Encoding.UTF8.GetString(stoppedOutput));
        Assert.AreEqual(69_001UL, stoppedStats.BytesSearched);
    }

    /// <summary>
    /// Verifies converted binary output keeps discontiguous matches adjacent when no context was requested.
    /// </summary>
    [TestMethod]
    public void BufferedBinarySearchOmitsContextSeparatorsBetweenMatches()
    {
        byte[] bytes = new byte[70_002];
        Array.Fill(bytes, (byte)'x');
        "needle\nother\nneedle\n"u8.CopyTo(bytes);
        bytes[65_535] = (byte)'\n';
        bytes[70_000] = 0;
        bytes[70_001] = (byte)'\n';

        (bool matched, byte[] output, _) = AssertSingleTraversal(
            expression: "needle",
            inputBytes: bytes,
            textMode: false,
            expectedMatchedLines: 2,
            expectedMatches: 2);

        Assert.IsTrue(matched);
        Assert.AreEqual(
            "1:needle\n3:needle\nbinary file matches (found \"\\0\" byte around offset 70000)\n",
            Encoding.UTF8.GetString(output));
    }

    /// <summary>
    /// Verifies multiline max-count retains every authoritative match on the selected line prefix.
    /// </summary>
    /// <param name="convertedBinary">Whether the input uses converted binary handling.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultilineMaxCountCountsMatchesOnRetainedLines(
        bool convertedBinary)
    {
        byte[] bytes;
        if (convertedBinary)
        {
            bytes = new byte[70_010];
            bytes.AsSpan().Fill((byte)'x');
            "foo foo\nfoo\n"u8.CopyTo(bytes);
            bytes[65_535] = (byte)'\n';
            bytes[69_000] = 0;
        }
        else
        {
            bytes = "foo foo\nfoo\n"u8.ToArray();
        }

        (bool matched, byte[] output, _) = AssertSingleTraversal(
            expression: "foo",
            inputBytes: bytes,
            textMode: !convertedBinary,
            multiline: true,
            maxCount: 1,
            expectedMatchedLines: 1,
            expectedMatches: 2);

        Assert.IsTrue(matched);
        Assert.StartsWith("1:foo foo\n", Encoding.UTF8.GetString(output));
        Assert.DoesNotContain("2:foo", Encoding.UTF8.GetString(output), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies one retained multiline discovery hit emits and counts every physical line it touches.
    /// </summary>
    /// <param name="convertedBinary">Whether the input uses converted binary handling.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultilineMaxCountRetainsCompleteMatchBlock(
        bool convertedBinary)
    {
        byte[] bytes;
        if (convertedBinary)
        {
            bytes = new byte[70_010];
            bytes.AsSpan().Fill((byte)'x');
            "foo\nbar\nfoo\nbar\n"u8.CopyTo(bytes);
            bytes[65_535] = (byte)'\n';
            bytes[69_000] = 0;
        }
        else
        {
            bytes = "foo\nbar\nfoo\nbar\n"u8.ToArray();
        }

        (bool matched, byte[] output, _) = AssertSingleTraversal(
            expression: "foo\\nbar",
            inputBytes: bytes,
            textMode: !convertedBinary,
            multiline: true,
            maxCount: 1,
            expectedMatchedLines: 2,
            expectedMatches: 1);

        string outputText = Encoding.UTF8.GetString(output);
        Assert.IsTrue(matched);
        Assert.StartsWith("1:foo\n2:bar\n", outputText);
        Assert.DoesNotContain("3:foo", outputText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch excludes non-empty matches beginning at its exclusive boundary.
    /// </summary>
    [TestMethod]
    public void MultilineStopExcludesBoundaryMatchAcrossRenderers()
    {
        const string Contents = "hit\nmiss\nnext\n";
        const string Expression = "hit|(?m:^next)";
        (bool plainMatched, byte[] plainOutput, _) = AssertSingleTraversal(
            contents: Contents,
            expression: Expression,
            multiline: true,
            stopOnNonmatch: true,
            expectedMatchedLines: 1,
            expectedMatches: 1);
        (bool colorMatched, byte[] colorOutput, _) = AssertSingleTraversal(
            contents: Contents,
            expression: Expression,
            color: true,
            multiline: true,
            stopOnNonmatch: true,
            expectedMatchedLines: 1,
            expectedMatches: 1);
        (bool replacementMatched, byte[] replacementOutput, _) = AssertSingleTraversal(
            contents: Contents,
            expression: Expression,
            replacement: "X"u8.ToArray(),
            multiline: true,
            stopOnNonmatch: true,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(plainMatched);
        Assert.IsTrue(colorMatched);
        Assert.IsTrue(replacementMatched);
        Assert.AreEqual("1:hit\n", Encoding.UTF8.GetString(plainOutput));
        Assert.Contains("hit", Encoding.UTF8.GetString(colorOutput), StringComparison.Ordinal);
        Assert.DoesNotContain("next", Encoding.UTF8.GetString(colorOutput), StringComparison.Ordinal);
        Assert.AreEqual("1:X\n", Encoding.UTF8.GetString(replacementOutput));
    }

    /// <summary>
    /// Verifies stop-on-nonmatch retains a valid empty match at the actual end of input.
    /// </summary>
    [TestMethod]
    public void MultilineStopRetainsUnterminatedEofEmptyMatch()
    {
        (bool matched, byte[] output, _) = AssertSingleTraversal(
            contents: "value",
            expression: @"\z",
            multiline: true,
            stopOnNonmatch: true,
            expectedMatchedLines: 1,
            expectedMatches: 1);

        Assert.IsTrue(matched);
        Assert.AreEqual("1:value\n", Encoding.UTF8.GetString(output));
    }

    /// <summary>
    /// Verifies inverted max-count statistics retain the traversal through the next matching line.
    /// </summary>
    [TestMethod]
    public void InvertedMaxCountPreservesContextTraversalExtent()
    {
        (bool matched, _, SearchStats stats) = AssertSingleTraversal(
            contents: "foo foo\nbar\nfoo\nlast\n",
            expression: "foo",
            invertMatch: true,
            maxCount: 1,
            stopOnNonmatch: true,
            expectedMatchedLines: 1,
            expectedMatches: 0);

        Assert.IsTrue(matched);
        Assert.AreEqual(16UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies direct inverted max-count statistics include the next positive record inspected by the matcher.
    /// </summary>
    [TestMethod]
    public void InvertedMaxCountDirectPathIncludesNextPositiveRecordInStats()
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            contents: "foo foo\nbar\nfoo\nlast\n",
            expression: "foo",
            invertMatch: true,
            maxCount: 1,
            expectedMatchedLines: 1,
            expectedMatches: 0);

        Assert.IsTrue(matched);
        Assert.AreEqual("2:bar\n", Encoding.UTF8.GetString(output));
        Assert.AreEqual(6UL, stats.BytesPrinted);
        Assert.AreEqual(16UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies inverted count output uses the same inspected extent as direct line output.
    /// </summary>
    [TestMethod]
    public void InvertedMaxCountCountPathIncludesNextPositiveRecordInStats()
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            contents: "foo foo\nbar\nfoo\nlast\n",
            expression: "foo",
            searchMode: CliSearchMode.Count,
            invertMatch: true,
            maxCount: 1,
            expectedMatchedLines: 1,
            expectedMatches: 0);

        Assert.IsTrue(matched);
        Assert.AreEqual("1\n", Encoding.UTF8.GetString(output));
        Assert.AreEqual(16UL, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies inverted max-count scans a long gap through the next positive record, or through EOF.
    /// </summary>
    [TestMethod]
    [DataRow("foo\nbar\none\ntwo\nthree\nfoo\nlast\n", 26UL)]
    [DataRow("foo\nbar\none\ntwo\n", 16UL)]
    public void InvertedMaxCountDirectPathRetainsLookaheadExtent(
        string contents,
        ulong bytesSearched)
    {
        (bool matched, byte[] output, SearchStats stats) = AssertSingleTraversal(
            contents,
            expression: "foo",
            invertMatch: true,
            maxCount: 1,
            expectedMatchedLines: 1,
            expectedMatches: 0);

        Assert.IsTrue(matched);
        Assert.AreEqual("2:bar\n", Encoding.UTF8.GetString(output));
        Assert.AreEqual(bytesSearched, stats.BytesSearched);
    }

    /// <summary>
    /// Verifies inverted direct statistics retain full traversal without a limit and skip traversal at zero.
    /// </summary>
    [TestMethod]
    public void InvertedDirectPathPreservesLimitBoundaryExtents()
    {
        const string Contents = "foo foo\nbar\nfoo\nlast\n";
        (bool unlimitedMatched, byte[] unlimitedOutput, SearchStats unlimitedStats) =
            AssertSingleTraversal(
                contents: Contents,
                expression: "foo",
                invertMatch: true,
                expectedMatchedLines: 2,
                expectedMatches: 0);
        (bool zeroMatched, byte[] zeroOutput, SearchStats zeroStats) =
            AssertSingleTraversal(
                contents: Contents,
                expression: "foo",
                invertMatch: true,
                maxCount: 0,
                expectedMatchedLines: 0,
                expectedMatches: 0);

        Assert.IsTrue(unlimitedMatched);
        Assert.AreEqual("2:bar\n4:last\n", Encoding.UTF8.GetString(unlimitedOutput));
        Assert.AreEqual(21UL, unlimitedStats.BytesSearched);
        Assert.IsFalse(zeroMatched);
        Assert.IsEmpty(zeroOutput);
        Assert.AreEqual(0UL, zeroStats.BytesSearched);
    }

    /// <summary>
    /// Verifies the stats path has no independent collection traversal and multiline renderers consume retained matches.
    /// </summary>
    [TestMethod]
    public void StatsAndMultilineRenderingHaveNoSecondTraversalEntryPoint()
    {
        string root = FindRepositoryRoot();
        string standardSource = File.ReadAllText(Path.Join(
            root,
            "src",
            "Scout.App",
            "StandardSearchByteOperations.cs"));
        string multilineSource = File.ReadAllText(Path.Join(
            root,
            "src",
            "Scout.App",
            "MultilineSearchOperations.cs"));

        Assert.DoesNotContain("CollectSearchStats", standardSource, StringComparison.Ordinal);
        Assert.Contains("metrics.ValidateCompletedTraversal();", standardSource, StringComparison.Ordinal);
        Assert.Contains("MultilineSearchResult searchResult", multilineSource, StringComparison.Ordinal);
        Assert.Contains("List<RegexMatch> matches = searchResult.Matches", multilineSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (!TryFindMultilineMatch(searchSpan",
            multilineSource,
            StringComparison.Ordinal);
    }

    private static (bool Matched, byte[] Output, SearchStats Stats) AssertSingleTraversal(
        string contents = "alpha alpha\nbeta\nalpha\n",
        string expression = "alpha",
        CliSearchMode searchMode = CliSearchMode.Standard,
        bool onlyMatching = false,
        ReadOnlyMemory<byte>? replacement = null,
        bool color = false,
        bool invertMatch = false,
        bool quiet = false,
        ulong beforeContext = 0,
        ulong afterContext = 0,
        bool passthru = false,
        ulong? maxCount = null,
        bool stopOnNonmatch = false,
        bool multiline = false,
        byte[]? inputBytes = null,
        bool textMode = true,
        bool memoryMapped = false,
        StandardBinaryDetectionScope binaryDetectionScope = StandardBinaryDetectionScope.WholeInput,
        ulong expectedMatchedLines = 2,
        ulong expectedMatches = 3)
    {
        byte[] bytes = inputBytes ?? Encoding.UTF8.GetBytes(contents);
        byte[][] patterns = [Encoding.UTF8.GetBytes(expression)];
        RegexSearchPlan regexPlan = RegexSearchPlan.Create(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive: false,
                multiline: multiline))!;
        using var output = new MemoryStream();
        var writer = new RawByteWriter(output);
        var separators = new OutputSeparators(
            ":"u8.ToArray(),
            "-"u8.ToArray(),
            "--"u8.ToArray(),
            contextEnabled: true,
            "\n"u8.ToArray());
        var stats = new SearchStats();
        bool wroteHeadingOutput = false;
        bool matched = StandardSearchByteOperations.SearchBytesWithStats(
            bytes,
            patterns,
            writer,
            prefix: null,
            separators,
            new OutputLineLimit(maxColumns: null, preview: false),
            new OutputColor(color),
            searchMode,
            vimgrep: false,
            lineNumber: true,
            column: false,
            byteOffset: false,
            asciiCaseInsensitive: false,
            invertMatch,
            lineRegexp: false,
            wordRegexp: false,
            multiline,
            multilineDotall: false,
            onlyMatching,
            replacement,
            maxCount,
            textMode,
            quiet,
            trim: false,
            beforeContext,
            afterContext,
            passthru,
            includeZero: true,
            nullPathTerminator: false,
            stopOnNonmatch,
            quitOnBinary: false,
            heading: false,
            ref wroteHeadingOutput,
            ref stats,
            memoryMapped,
            regexPlan,
            binaryDetectionScope);
        writer.Flush();

        Assert.AreEqual(expectedMatchedLines, stats.MatchedLines);
        Assert.AreEqual(expectedMatches, stats.Matches);
        Assert.AreEqual(1UL, stats.Searches);
        return (matched, output.ToArray(), stats);
    }

    private static byte[] CreateLateBinaryInput(bool includeTrailingMatch)
    {
        int length = includeTrailingMatch ? 70_008 : 70_001;
        byte[] bytes = new byte[length];
        bytes.AsSpan().Fill((byte)'x');
        "needle\n"u8.CopyTo(bytes);
        bytes[69_000] = 0;
        bytes[70_000] = (byte)'\n';
        if (includeTrailingMatch)
        {
            "needle\n"u8.CopyTo(bytes.AsSpan(70_001));
        }

        return bytes;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the Scout repository root.");
    }
}
