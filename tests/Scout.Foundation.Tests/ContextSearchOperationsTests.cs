using System.Text;

namespace Scout;

/// <summary>
/// Verifies context-search matching and planning behavior.
/// </summary>
[TestClass]
public sealed class ContextSearchOperationsTests
{
    /// <summary>
    /// Verifies discontiguous selected lines do not receive context separators when no context was requested.
    /// </summary>
    [TestMethod]
    public void SearchBytesOmitsSeparatorsBetweenMatchesWithoutContext()
    {
        byte[] bytes = "needle\nother\nneedle\n"u8.ToArray();
        byte[][] patterns = ["needle"u8.ToArray()];
        using var output = new MemoryStream();
        var writer = new RawByteWriter(output);
        var separators = new OutputSeparators(
            ":"u8.ToArray(),
            "-"u8.ToArray(),
            "--"u8.ToArray(),
            contextEnabled: true,
            "\n"u8.ToArray());

        bool matched = ContextSearchOperations.SearchBytes(
            bytes,
            patterns,
            writer,
            prefix: null,
            separators,
            new OutputLineLimit(maxColumns: null, preview: false),
            new OutputColor(enabled: false),
            lineNumber: true,
            column: false,
            byteOffset: false,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            vimgrep: false,
            onlyMatching: false,
            replacement: null,
            maxCount: null,
            trim: false,
            beforeContext: 0,
            afterContext: 0,
            passthru: false,
            nullPathTerminator: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));
        writer.Flush();

        Assert.IsTrue(matched);
        Assert.AreEqual(
            "1:needle\n3:needle\n",
            Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// Verifies every context renderer replays one authoritative traversal's retained spans.
    /// </summary>
    /// <param name="vimgrep">Whether vimgrep output is requested.</param>
    /// <param name="onlyMatching">Whether only matching spans are requested.</param>
    /// <param name="replace">Whether captures are expanded into replacements.</param>
    /// <param name="color">Whether retained spans are highlighted.</param>
    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(false, true, false, false)]
    [DataRow(false, false, true, false)]
    [DataRow(true, false, false, false)]
    [DataRow(true, false, true, true)]
    [DataRow(false, false, false, true)]
    public void SearchBytesReplaysAuthoritativeSpansAcrossRenderingModes(
        bool vimgrep,
        bool onlyMatching,
        bool replace,
        bool color)
    {
        byte[] bytes =
            "before\nab12 ------- ab34 ------- ab56\nafter\n"u8.ToArray();
        byte[][] patterns =
            ["(?<word>ab)(?<digits>[0-9]+)"u8.ToArray()];
        ReadOnlyMemory<byte>? replacement = replace
            ? "$2-$1"u8.ToArray()
            : null;
        using var output = new MemoryStream();
        var writer = new RawByteWriter(output);
        var separators = new OutputSeparators(
            ":"u8.ToArray(),
            "-"u8.ToArray(),
            "--"u8.ToArray(),
            contextEnabled: true,
            "\n"u8.ToArray());
        bool matched = ContextSearchOperations.SearchBytes(
            bytes,
            patterns,
            writer,
            prefix: null,
            separators,
            new OutputLineLimit(maxColumns: 12, preview: true),
            new OutputColor(color),
            lineNumber: true,
            column: true,
            byteOffset: true,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            vimgrep,
            onlyMatching,
            replacement,
            maxCount: null,
            trim: false,
            beforeContext: 1,
            afterContext: 1,
            passthru: false,
            nullPathTerminator: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));
        writer.Flush();

        Assert.IsTrue(matched);
        Assert.IsNotEmpty(output.ToArray());
    }

    /// <summary>
    /// Verifies replacement replay transforms original-match context records during inverted searches.
    /// </summary>
    /// <param name="vimgrep">Whether vimgrep records are requested.</param>
    /// <param name="color">Whether replacement output is colored.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void SearchBytesReplacesOriginalMatchContextDuringInvertedSearch(
        bool vimgrep,
        bool color)
    {
        byte[] bytes = "prefix foo suffix\nbar\n"u8.ToArray();
        byte[][] patterns = ["foo"u8.ToArray()];
        using var output = new MemoryStream();
        var writer = new RawByteWriter(output);
        var separators = new OutputSeparators(
            ":"u8.ToArray(),
            "-"u8.ToArray(),
            "--"u8.ToArray(),
            contextEnabled: true,
            "\n"u8.ToArray());

        bool matched = ContextSearchOperations.SearchBytes(
            bytes,
            patterns,
            writer,
            prefix: null,
            separators,
            new OutputLineLimit(maxColumns: null, preview: false),
            new OutputColor(color),
            lineNumber: false,
            column: false,
            byteOffset: false,
            asciiCaseInsensitive: false,
            invertMatch: true,
            lineRegexp: false,
            wordRegexp: false,
            vimgrep,
            onlyMatching: false,
            replacement: "X"u8.ToArray(),
            maxCount: null,
            trim: false,
            beforeContext: 1,
            afterContext: 0,
            passthru: false,
            nullPathTerminator: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));
        writer.Flush();

        string text = Encoding.UTF8.GetString(output.ToArray());
        Assert.IsTrue(matched);
        Assert.Contains("prefix", text, StringComparison.Ordinal);
        Assert.Contains("X", text, StringComparison.Ordinal);
        Assert.Contains("suffix", text, StringComparison.Ordinal);
        Assert.DoesNotContain("foo", text, StringComparison.Ordinal);
        Assert.Contains("bar", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies context output has one authoritative matcher entry point and no rendering-time search.
    /// </summary>
    [TestMethod]
    public void ContextOutputHasOneAuthoritativeMatcherEntryPoint()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Join(
            root,
            "src",
            "Scout.App",
            "ContextSearchOperations.cs"));

        Assert.AreEqual(
            1,
            CountOccurrences(source, "SearchMatchLinesWithRegexPlan("));
        int writerStart = source.IndexOf(
            "internal static bool WriteSearchResult(",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, writerStart);
        int writerEnd = source.IndexOf(
            "internal static ContextSearchResult BuildSearchResult(",
            writerStart,
            StringComparison.Ordinal);
        Assert.IsGreaterThan(writerStart, writerEnd);
        Assert.DoesNotContain(
            "LiteralLineSearcher.",
            source[writerStart..writerEnd],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the context result retains every authoritative span in report order.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultRetainsOrderedAuthoritativeSpans()
    {
        byte[] bytes = "ab12 ab34\n"u8.ToArray();
        byte[][] patterns = ["[a-z]+|[0-9]+"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            bytes,
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));

        ContextLineInfo line = Assert.ContainsSingle(result.Lines);
        ContextLineMatch[] matches = result.GetMatches(line).ToArray();
        Assert.HasCount(4, matches);
        Assert.AreSequenceEqual([0, 2, 5, 7], matches.Select(match => match.Start));
        Assert.AreSequenceEqual([1L, 3L, 6L, 8L], matches.Select(match => match.Column));
        Assert.AreSequenceEqual([2, 2, 2, 2], matches.Select(match => match.Length));
    }

    /// <summary>
    /// Verifies whole-buffer context matching does not treat the line terminator as match content.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultExcludesLineTerminatorFromRegexMatching()
    {
        byte[][] patterns = ["foo[^x]"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "foo\n"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));

        ContextLineInfo line = Assert.ContainsSingle(result.Lines);
        Assert.IsFalse(line.SelectedMatch);
        Assert.IsFalse(line.OriginalMatch);
    }

    /// <summary>
    /// Verifies whole-buffer context matching selects a zero-width physical-end match without a
    /// synthetic column on the unterminated final line.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultPreservesZeroWidthMatchOnUnterminatedFinalLine()
    {
        byte[][] patterns = ["$"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "value"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: false,
            regexPlan: CreateRegexPlan(patterns));

        ContextLineInfo line = Assert.ContainsSingle(result.Lines);
        Assert.IsTrue(line.SelectedMatch);
        Assert.IsTrue(line.OriginalMatch);
        Assert.AreEqual(0, line.MatchColumn);
        Assert.AreEqual(0, line.ContextColumn);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch does not retain an authoritative anchored-match tail or consult
    /// the raw pattern list after plan construction.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultStopsAuthoritativeCandidatesBeforeLargeTail()
    {
        const int TailLineCount = 50_000;
        var patterns = new CountingPatternList([@"\Afoo"u8.ToArray()]);
        var regexPlan = RegexSearchPlan.Create(
            patterns,
            asciiCaseInsensitive: false);
        byte[] prefix = "foo\nmiss\n"u8.ToArray();
        byte[] tailLine = "foo\n"u8.ToArray();
        byte[] bytes = new byte[prefix.Length + (TailLineCount * tailLine.Length)];
        prefix.CopyTo(bytes, 0);
        for (int offset = prefix.Length; offset < bytes.Length; offset += tailLine.Length)
        {
            tailLine.CopyTo(bytes, offset);
        }

        patterns.ResetAccessCount();
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            bytes,
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan);

        Assert.AreEqual(0, patterns.AccessCount);
        Assert.HasCount(2, result.Lines);
        Assert.IsTrue(result.Lines[0].SelectedMatch);
        Assert.IsFalse(result.Lines[1].SelectedMatch);
        Assert.ContainsSingle(result.GetMatches(result.Lines[0]).ToArray());
    }

    /// <summary>
    /// Verifies an inverted stop includes the first unselected record and its original spans.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultRetainsInvertedStopRecordMatches()
    {
        byte[][] patterns = ["foo"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "miss\nfoo\ntail\n"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: true,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan: CreateRegexPlan(patterns));

        Assert.HasCount(2, result.Lines);
        Assert.IsTrue(result.Lines[0].SelectedMatch);
        Assert.IsFalse(result.Lines[0].OriginalMatch);
        Assert.IsFalse(result.Lines[1].SelectedMatch);
        Assert.IsTrue(result.Lines[1].OriginalMatch);
        ContextLineMatch match = Assert.ContainsSingle(
            result.GetMatches(result.Lines[1]).ToArray());
        Assert.AreEqual(0, match.Start);
        Assert.AreEqual(3, match.Length);
    }

    /// <summary>
    /// Verifies record-relative absolute-anchor selection retains only prefix-valid spans.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultPreservesAbsoluteAnchorSelectionSpans()
    {
        byte[][] patterns = [@"\A"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "a\nb\n"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan: CreateRegexPlan(patterns));

        Assert.HasCount(2, result.Lines);
        TestAssert.All(result.Lines, line => Assert.IsTrue(line.OriginalMatch));
        Assert.ContainsSingle(result.GetMatches(result.Lines[0]).ToArray());
        Assert.IsEmpty(result.GetMatches(result.Lines[1]).ToArray());
    }

    /// <summary>
    /// Verifies a zero-width selected record stops after retaining its following non-match.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultStopsAfterEmptySelectedRecord()
    {
        byte[][] patterns = ["^$"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "\nvalue\n\n"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan: CreateRegexPlan(patterns));

        Assert.HasCount(2, result.Lines);
        Assert.IsTrue(result.Lines[0].SelectedMatch);
        Assert.IsFalse(result.Lines[1].SelectedMatch);
        ContextLineMatch match = Assert.ContainsSingle(
            result.GetMatches(result.Lines[0]).ToArray());
        Assert.AreEqual(0, match.Start);
        Assert.AreEqual(0, match.Length);
    }

    /// <summary>
    /// Verifies an unterminated end-empty selection is retained without a synthetic span.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultRetainsStoppedSelectionOnlyEndEmptyMatch()
    {
        byte[][] patterns = ["$"u8.ToArray()];
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "value"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan: CreateRegexPlan(patterns));

        ContextLineInfo line = Assert.ContainsSingle(result.Lines);
        Assert.IsTrue(line.SelectedMatch);
        Assert.IsTrue(line.OriginalMatch);
        Assert.IsEmpty(result.GetMatches(line).ToArray());
    }

    /// <summary>
    /// Verifies CRLF-preserving selection stops without manufacturing a reportable carriage-return span.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultPreservesCrlfSelectionOnlyStop()
    {
        byte[][] patterns = ["foo\\r"u8.ToArray()];
        var options = new RegexSearchPlanOptions(
            asciiCaseInsensitive: false,
            crlf: true,
            preserveCrlfCarriageReturn: true);
        var regexPlan = RegexSearchPlan.Create(patterns, options);
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "foo\r\nmiss\nfoo\r\n"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: true,
            nullData: false,
            stopOnNonmatch: true,
            regexPlan);

        Assert.HasCount(2, result.Lines);
        Assert.IsTrue(result.Lines[0].SelectedMatch);
        Assert.IsFalse(result.Lines[1].SelectedMatch);
        Assert.IsEmpty(result.GetMatches(result.Lines[0]).ToArray());
    }

    /// <summary>
    /// Verifies NUL-delimited stopping includes one non-match and excludes the remaining records.
    /// </summary>
    [TestMethod]
    public void BuildSearchResultStopsAtNulRecordBoundary()
    {
        byte[][] patterns = ["foo"u8.ToArray()];
        var options = new RegexSearchPlanOptions(
            asciiCaseInsensitive: false,
            nullData: true);
        var regexPlan = RegexSearchPlan.Create(patterns, options);
        ContextSearchResult result = ContextSearchOperations.BuildSearchResult(
            "foo\0miss\0foo\0"u8.ToArray(),
            patterns,
            asciiCaseInsensitive: false,
            invertMatch: false,
            lineRegexp: false,
            wordRegexp: false,
            crlf: false,
            nullData: true,
            stopOnNonmatch: true,
            regexPlan);

        Assert.HasCount(2, result.Lines);
        Assert.AreEqual(4, result.Lines[0].Length);
        Assert.AreEqual(5, result.Lines[1].Length);
        Assert.IsTrue(result.Lines[0].SelectedMatch);
        Assert.IsFalse(result.Lines[1].SelectedMatch);
        ContextLineMatch match = Assert.ContainsSingle(
            result.GetMatches(result.Lines[0]).ToArray());
        Assert.AreEqual(0, match.Start);
        Assert.AreEqual(3, match.Length);
    }

    private static int CountOccurrences(string value, string text)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(text, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += text.Length;
        }

        return count;
    }

    private static RegexSearchPlan CreateRegexPlan(IReadOnlyList<byte[]> patterns)
    {
        return RegexSearchPlan.Create(patterns, asciiCaseInsensitive: false);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory, "Scout.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
