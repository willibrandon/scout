using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Scout;

/// <summary>
/// Verifies runtime search modes and parity-heavy application behavior.
/// </summary>
public sealed class ScoutApplicationRuntimeTests
{
    /// <summary>
    /// Verifies JSON output matches pinned ripgrep after normalizing runtime-only timing fields.
    /// </summary>
    [Fact]
    public void JsonOutputMatchesPinnedRipgrepForLiteralSearch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nbeta needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default multi-threaded JSON directory search emits the same messages as ripgrep.
    /// </summary>
    [Fact]
    public void JsonDefaultThreadsSearchDirectoryLikeRipgrep()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, "left", "one.txt"), "needle one\n");
        File.WriteAllText(Path.Combine(root, "left", "drop.log"), "needle drop\n");
        File.WriteAllText(Path.Combine(root, "right", "two.txt"), "needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(SortedNormalizedJsonMessages(pinnedOutput), SortedNormalizedJsonMessages(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON context output and replacement metadata match pinned ripgrep.
    /// </summary>
    [Fact]
    public void JsonOutputIncludesContextAndReplacementMetadata()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one needle\nbeta\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-B1", "-r", "X", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-B1", "-r", "X", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata expands numeric captures.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsNumericCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabc456\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-r", "$2-$1", "([a-z]+)([0-9]+)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-r", "$2-$1", "([a-z]+)([0-9]+)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata expands named captures.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsNamedCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabc456\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-r", "$digits-$word", "(?P<word>[a-z]+)(?P<digits>[0-9]+)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-r", "$digits-$word", "(?P<word>[a-z]+)(?P<digits>[0-9]+)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata preserves named captures across alternation branches.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsAlternationCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-r", "$left:$right:$0", "(?P<left>foo)|(?P<right>bar)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-r", "$left:$right:$0", "(?P<left>foo)|(?P<right>bar)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata expands captures from patterns with inline regex flags.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsInlineFlagCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "FOO\nfoo\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-r", "$word", "(?i)(?P<word>foo)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-r", "$word", "(?i)(?P<word>foo)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata backtracks quantified captures.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsBacktrackedQuantifiedCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "aaa\naaab\nab\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-r", "$1:$2:$0", "(a+)(a)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-r", "$1:$2:$0", "(a+)(a)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON replacement metadata expands shorthand and POSIX regex class captures.
    /// </summary>
    [Fact]
    public void JsonOutputReplacementMetadataExpandsRegexClassCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabcXYZ\n");

        (int shorthandExitCode, byte[] shorthandOutput, string shorthandError) = RunScout("--json", "-r", "$1:$2:$0", @"([a-z]+)(\d+)", path);
        (int pinnedShorthandExitCode, byte[] pinnedShorthandOutput, string pinnedShorthandError) = RunPinnedRipgrep("--json", "-r", "$1:$2:$0", @"([a-z]+)(\d+)", path);
        (int posixExitCode, byte[] posixOutput, string posixError) = RunScout("--json", "-r", "$1:$2:$0", "([[:alpha:]]+)([[:digit:]]+)", path);
        (int pinnedPosixExitCode, byte[] pinnedPosixOutput, string pinnedPosixError) = RunPinnedRipgrep("--json", "-r", "$1:$2:$0", "([[:alpha:]]+)([[:digit:]]+)", path);

        Assert.Equal(pinnedShorthandExitCode, shorthandExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedShorthandOutput), NormalizeJsonTimings(shorthandOutput));
        Assert.Equal(pinnedShorthandError, shorthandError);
        Assert.Equal(pinnedPosixExitCode, posixExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedPosixOutput), NormalizeJsonTimings(posixOutput));
        Assert.Equal(pinnedPosixError, posixError);
    }

    /// <summary>
    /// Verifies quiet JSON mode suppresses per-file messages but still emits the summary.
    /// </summary>
    [Fact]
    public void JsonQuietPrintsSummaryOnly()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("-q", "--json", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "--json", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON data switches to base64 bytes when a matching line is not valid UTF-8.
    /// </summary>
    [Fact]
    public void JsonInvalidUtf8LineUsesBytesData()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, [0xFF, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e', (byte)'\n']);

        (int exitCode, byte[] output, string error) = RunScout("--json", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON binary output reports the binary offset and searches with NUL converted to a line break.
    /// </summary>
    [Fact]
    public void JsonBinaryOutputReportsBinaryOffset()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0bbb\n"));

        (int exitCode, byte[] output, string error) = RunScout("--json", "bbb", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "bbb", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON text mode preserves NUL bytes and suppresses binary-offset metadata.
    /// </summary>
    [Fact]
    public void JsonBinaryTextModeKeepsNulInLineData()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0bbb\n"));

        (int exitCode, byte[] output, string error) = RunScout("--json", "-a", "bbb", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-a", "bbb", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies quiet JSON binary searches still contribute converted-line stats to the summary.
    /// </summary>
    [Fact]
    public void JsonBinaryQuietModeSummarizesConvertedMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0bbb\n"));

        (int exitCode, byte[] output, string error) = RunScout("-q", "--json", "bbb", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "--json", "bbb", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies stats output matches pinned ripgrep after normalizing runtime-only timing fields.
    /// </summary>
    [Fact]
    public void StatsOutputMatchesPinnedRipgrepForLiteralSearch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nbeta needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--stats", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stats", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeStatsTimings(pinnedOutput), NormalizeStatsTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies stats output is printed for no-match searches.
    /// </summary>
    [Fact]
    public void StatsOutputMatchesPinnedRipgrepForNoMatch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\n");

        (int exitCode, byte[] output, string error) = RunScout("--stats", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stats", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeStatsTimings(pinnedOutput), NormalizeStatsTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies quiet stats suppress matching output but still print stats.
    /// </summary>
    [Fact]
    public void StatsQuietMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nbeta needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--stats", "-q", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stats", "-q", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeStatsTimings(pinnedOutput), NormalizeStatsTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies stats use summary-printer byte counting for count mode.
    /// </summary>
    [Fact]
    public void StatsCountModeMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nbeta needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--stats", "-c", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stats", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeStatsTimings(pinnedOutput), NormalizeStatsTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies no-stats disables an earlier stats flag.
    /// </summary>
    [Fact]
    public void NoStatsDisablesStatsOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--stats", "--no-stats", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stats", "--no-stats", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies debug logging uses Scout-owned diagnostic identity for a single file.
    /// </summary>
    [Fact]
    public void DebugLoggingUsesScoutDiagnosticIdentityForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--debug", "--no-config", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, _) = RunPinnedRipgrep("--debug", "--no-config", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Contains($"scout: DEBUG|Scout.App.Flags|{ScoutSource("src/Scout.App/ConfigArgumentExpander.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"scout: DEBUG|Scout.App.Flags|{ScoutSource("src/Scout.App/SearchDiagnosticLogging.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"scout: DEBUG|Scout.Regex|{ScoutSource("src/Scout.App/SearchDiagnosticLogging.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains("assembling regex program from 1 pattern(s)", error, StringComparison.Ordinal);
        Assert.DoesNotContain("fixed string literals", error, StringComparison.Ordinal);
        Assert.DoesNotContain("rg::", error, StringComparison.Ordinal);
        Assert.DoesNotContain("crates/", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies debug logging reports the ignore files and rules that prune directory entries.
    /// </summary>
    [Fact]
    public void DebugLoggingReportsDirectoryIgnoreDecisions()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        string ignoredDirectory = Path.Combine(root, "ignored");
        Directory.CreateDirectory(ignoredDirectory);
        string ignoreFile = Path.Combine(root, ".gitignore");
        File.WriteAllText(ignoreFile, "ignored/\n");
        File.WriteAllText(Path.Combine(ignoredDirectory, "hit.txt"), "needle\n");
        File.WriteAllText(Path.Combine(root, "keep.txt"), "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--debug", "--no-config", "--threads", "1", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, _) = RunPinnedRipgrep("--debug", "--no-config", "--threads", "1", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Contains($"scout: DEBUG|Scout.Ignore|{ScoutSource("src/Scout.Ignore/IgnoreStack.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"opened ignore file: {ignoreFile}", error, StringComparison.Ordinal);
        Assert.Contains($"scout: DEBUG|Scout.Globbing|{ScoutSource("src/Scout.Ignore/IgnoreStack.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains("built glob set;", error, StringComparison.Ordinal);
        Assert.Contains($"scout: DEBUG|Scout.Ignore|{ScoutSource("src/Scout.Ignore/Walk.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"matched ignore rule from {ignoreFile}: \"ignored/\" -> \"ignored\", directory-only", error, StringComparison.Ordinal);
        Assert.DoesNotContain("rg::", error, StringComparison.Ordinal);
        Assert.DoesNotContain("crates/", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies trace logging uses Scout-owned diagnostic identity for a single file.
    /// </summary>
    [Fact]
    public void TraceLoggingUsesScoutDiagnosticIdentityForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--trace", "--no-config", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, _) = RunPinnedRipgrep("--trace", "--no-config", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Contains($"scout: DEBUG|Scout.App.Flags|{ScoutSource("src/Scout.App/ConfigArgumentExpander.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"scout: TRACE|Scout.Regex|{ScoutSource("src/Scout.App/SearchDiagnosticLogging.cs")}:", error, StringComparison.Ordinal);
        Assert.Contains($"scout: TRACE|Scout.Searching|{ScoutSource("src/Scout.App/SearchDiagnosticLogging.cs")}:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("rg::", error, StringComparison.Ordinal);
        Assert.DoesNotContain("crates/", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies thread-count flags search with ripgrep-compatible output.
    /// </summary>
    [Fact]
    public void ThreadsFlagSearchesLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("--threads", "2", "-j=1", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--threads", "2", "-j=1", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies explicit multi-threaded directory search finds the same matches as ripgrep.
    /// </summary>
    [Fact]
    public void ThreadsFlagSearchesDirectoryLikeRipgrep()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, "left", "one.txt"), "needle one\n");
        File.WriteAllText(Path.Combine(root, "left", "drop.log"), "needle drop\n");
        File.WriteAllText(Path.Combine(root, "right", "two.txt"), "needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--threads", "2", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--threads", "2", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(SortedUtf8Lines(pinnedOutput), SortedUtf8Lines(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default multi-threaded directory search finds the same matches as ripgrep.
    /// </summary>
    [Fact]
    public void DefaultThreadsSearchDirectoryLikeRipgrep()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, "left", "one.txt"), "needle one\n");
        File.WriteAllText(Path.Combine(root, "left", "drop.log"), "needle drop\n");
        File.WriteAllText(Path.Combine(root, "right", "two.txt"), "needle two\n");

        (int exitCode, byte[] output, string error) = RunScout("needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(SortedUtf8Lines(pinnedOutput), SortedUtf8Lines(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies buffering and memory-map flags search with ripgrep-compatible output.
    /// </summary>
    [Fact]
    public void BufferingAndMmapFlagsSearchLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("--line-buffered", "--block-buffered", "--no-block-buffered", "--mmap", "--no-mmap", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--line-buffered", "--block-buffered", "--no-block-buffered", "--mmap", "--no-mmap", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies explicit files larger than the managed array limit use the streaming reader path.
    /// </summary>
    [Fact]
    public void LargeExplicitFileSearchStreamsLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "large.txt");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            stream.Write("alpha\nneedle\n"u8);
            stream.SetLength((long)int.MaxValue + 4096);
        }

        (int exitCode, byte[] output, string error) = RunScout("--no-config", "--mmap", "-a", "-m", "1", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-config", "--mmap", "-a", "-m", "1", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large files reached by recursive search can use the streaming path without changing output.
    /// </summary>
    [Fact]
    public void LargeImplicitDirectoryFileSearchesLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "large.txt");
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            for (int index = 0; index < 150_000; index++)
            {
                writer.WriteLine("alpha");
            }

            writer.WriteLine("needle");
        }

        (int exitCode, byte[] output, string error) = RunScout("--no-config", "--threads", "2", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-config", "--threads", "2", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies implicit streaming is bypassed for BOM-bearing files that need decoding.
    /// </summary>
    [Fact]
    public void LargeImplicitBomFileSearchesLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "utf16.txt");
        string body = new string('a', 600_000) + "\nneedle\n";
        File.WriteAllText(path, body, Encoding.Unicode);

        (int exitCode, byte[] output, string error) = RunScout("--no-config", "--threads", "2", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-config", "--threads", "2", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies newly covered parser flags preserve literal-search parity when their behavior is neutral.
    /// </summary>
    [Fact]
    public void RemainingNonGenerateFlagsSearchLikeRipgrepWhenNeutral()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        string[] arguments =
        [
            "--no-fixed-strings",
            "--dfa-size-limit=1M",
            "--regex-size-limit",
            "2M",
            "--multiline",
            "--multiline-dotall",
            "--no-multiline",
            "--no-multiline-dotall",
            "--no-unicode",
            "--unicode",
            "--color=never",
            "--colors",
            "match:fg:blue",
            "--hyperlink-format=none",
            "--hostname-bin",
            "does-not-run",
            "needle",
            path,
        ];

        (int exitCode, byte[] output, string error) = RunScout(arguments);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(arguments);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--no-messages</c> suppresses non-fatal file diagnostics while preserving exit status.
    /// </summary>
    [Fact]
    public void NoMessagesSuppressesMissingPathDiagnostic()
    {
        string root = CreateTempDirectory();
        string missing = Path.Combine(root, "missing.txt");

        (int exitCode, byte[] output, string error) = RunScout("--no-messages", "needle", missing);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-messages", "needle", missing);
        (int enabledExitCode, byte[] enabledOutput, string enabledError) = RunScout("--no-messages", "--messages", "needle", missing);
        (int pinnedEnabledExitCode, byte[] pinnedEnabledOutput, string pinnedEnabledError) = RunPinnedRipgrep("--no-messages", "--messages", "needle", missing);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedEnabledExitCode, enabledExitCode);
        Assert.Equal(pinnedEnabledOutput, enabledOutput);
        Assert.Equal(pinnedEnabledError, enabledError);
    }

    /// <summary>
    /// Verifies forcing PCRE2 fails like the pinned non-PCRE2 ripgrep build.
    /// </summary>
    [Fact]
    public void Pcre2UnavailableDiagnosticMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("-P", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-P", "needle", path);
        (int engineExitCode, byte[] engineOutput, string engineError) = RunScout("--engine=pcre2", "needle", path);
        (int pinnedEngineExitCode, byte[] pinnedEngineOutput, string pinnedEngineError) = RunPinnedRipgrep("--engine=pcre2", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedEngineExitCode, engineExitCode);
        Assert.Equal(pinnedEngineOutput, engineOutput);
        Assert.Equal(pinnedEngineError, engineError);
    }

    /// <summary>
    /// Verifies non-PCRE2 regex engine flags search with ripgrep-compatible output.
    /// </summary>
    [Fact]
    public void RegexEngineNonPcre2FlagsSearchLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("--engine", "auto", "--auto-hybrid-regex", "--no-auto-hybrid-regex", "--no-pcre2", "--no-pcre2-unicode", "--pcre2-unicode", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--engine", "auto", "--auto-hybrid-regex", "--no-auto-hybrid-regex", "--no-pcre2", "--no-pcre2-unicode", "--pcre2-unicode", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>-z</c> decompresses gzip files through the configured external tool.
    /// </summary>
    [Fact]
    public void SearchZipDecompressesGzipFiles()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.gz");
        WriteGzipFile(path, "alpha\nneedle in gzip\n");

        (int exitCode, byte[] output, string error) = RunScout("-z", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-z", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>-z</c> uses the pinned ripgrep default decompression command table.
    /// </summary>
    [Fact]
    public void SearchZipDefaultDecompressionMatrixMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        byte[] contents = Encoding.UTF8.GetBytes("alpha\nneedle in compressed file\n");
        (string Path, string Program, string[] Arguments)[] cases =
        [
            (Path.Combine(root, "input.tgz"), "gzip", ["-c"]),
            (Path.Combine(root, "input.bz2"), "bzip2", ["-z", "-c"]),
            (Path.Combine(root, "input.tbz2"), "bzip2", ["-z", "-c"]),
            (Path.Combine(root, "input.xz"), "xz", ["-z", "-c", "--format=xz"]),
            (Path.Combine(root, "input.txz"), "xz", ["-z", "-c", "--format=xz"]),
            (Path.Combine(root, "input.lzma"), "xz", ["-z", "-c", "--format=lzma"]),
            (Path.Combine(root, "input.lz4"), "lz4", ["-z", "-c"]),
            (Path.Combine(root, "input.br"), "brotli", ["-c"]),
            (Path.Combine(root, "input.zst"), "zstd", ["-q", "-z", "-c"]),
            (Path.Combine(root, "input.zstd"), "zstd", ["-q", "-z", "-c"]),
            (Path.Combine(root, "input.Z"), "compress", ["-c"]),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            WriteCompressedFile(cases[index].Path, cases[index].Program, cases[index].Arguments, contents);

            (int exitCode, byte[] output, string error) = RunScout("-z", "needle", cases[index].Path);
            (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-z", "needle", cases[index].Path);

            Assert.Equal(pinnedExitCode, exitCode);
            Assert.Equal(pinnedOutput, output);
            Assert.Equal(pinnedError, error);
        }
    }

    /// <summary>
    /// Verifies <c>--no-search-zip</c> disables an earlier compressed-search flag.
    /// </summary>
    [Fact]
    public void NoSearchZipDisablesCompressedSearch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.gz");
        WriteGzipFile(path, "alpha\nneedle in gzip\n");

        (int exitCode, byte[] output, string error) = RunScout("-z", "--no-search-zip", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-z", "--no-search-zip", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--pre</c> searches command output for each file path.
    /// </summary>
    [Fact]
    public void PreprocessorSearchesCommandOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string script = CreatePreprocessorScript(root);
        File.WriteAllText(path, "alpha\n");

        (int exitCode, byte[] output, string error) = RunScout("--pre", script, "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--pre", script, "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--pre</c> failure diagnostics include ripgrep's rendered command and stderr block.
    /// </summary>
    [Fact]
    public void PreprocessorFailureDiagnosticMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string script = CreateFailingPreprocessorScript(root);
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--pre", script, "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--pre", script, "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--pre-glob</c> limits which paths run through the preprocessor.
    /// </summary>
    [Fact]
    public void PreprocessorGlobLimitsCommandExecution()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string script = CreatePreprocessorScript(root);
        File.WriteAllText(path, "alpha\n");

        (int exitCode, byte[] output, string error) = RunScout("--pre", script, "--pre-glob", "*.xz", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--pre", script, "--pre-glob", "*.xz", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies matching binary files print ripgrep's binary-file message by default.
    /// </summary>
    [Fact]
    public void BinaryFileDefaultPrintsBinaryMatchMessage()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("prefix needle\nalpha\0needle\nnext needle\n"));

        (int exitCode, byte[] output, string error) = RunScout("needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies text mode prints binary matching lines instead of the binary-file message.
    /// </summary>
    [Fact]
    public void BinaryFileTextModePrintsRawMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("prefix needle\nalpha\0needle\nnext needle\n"));

        (int exitCode, byte[] output, string error) = RunScout("-a", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-a", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies binary flags can disable or be overridden by text mode.
    /// </summary>
    [Fact]
    public void BinaryFileTextModeIsOrderSensitive()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("prefix needle\nalpha\0needle\nnext needle\n"));

        (int binaryExitCode, byte[] binaryOutput, string binaryError) = RunScout("-a", "--binary", "needle", path);
        (int pinnedBinaryExitCode, byte[] pinnedBinaryOutput, string pinnedBinaryError) = RunPinnedRipgrep("-a", "--binary", "needle", path);
        (int textExitCode, byte[] textOutput, string textError) = RunScout("--binary", "-a", "needle", path);
        (int pinnedTextExitCode, byte[] pinnedTextOutput, string pinnedTextError) = RunPinnedRipgrep("--binary", "-a", "needle", path);

        Assert.Equal(pinnedBinaryExitCode, binaryExitCode);
        Assert.Equal(pinnedBinaryOutput, binaryOutput);
        Assert.Equal(pinnedBinaryError, binaryError);
        Assert.Equal(pinnedTextExitCode, textExitCode);
        Assert.Equal(pinnedTextOutput, textOutput);
        Assert.Equal(pinnedTextError, textError);
    }

    /// <summary>
    /// Verifies recursive binary filtering differs from explicit binary searching.
    /// </summary>
    [Fact]
    public void RecursiveBinaryFilteringMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string after = Path.Combine(root, "after.dat");
        string before = Path.Combine(root, "before.dat");
        File.WriteAllBytes(after, Encoding.UTF8.GetBytes("alpha\0beta needle\n"));
        File.WriteAllBytes(before, Encoding.UTF8.GetBytes("needle alpha\0beta\n"));

        (int defaultExitCode, byte[] defaultOutput, string defaultError) = RunScout("--sort=path", "needle", root);
        (int pinnedDefaultExitCode, byte[] pinnedDefaultOutput, string pinnedDefaultError) = RunPinnedRipgrep("--sort=path", "needle", root);
        (int binaryExitCode, byte[] binaryOutput, string binaryError) = RunScout("--sort=path", "--binary", "needle", root);
        (int pinnedBinaryExitCode, byte[] pinnedBinaryOutput, string pinnedBinaryError) = RunPinnedRipgrep("--sort=path", "--binary", "needle", root);

        Assert.Equal(pinnedDefaultExitCode, defaultExitCode);
        Assert.Equal(pinnedDefaultOutput, defaultOutput);
        Assert.Equal(pinnedDefaultError, defaultError);
        Assert.Equal(pinnedBinaryExitCode, binaryExitCode);
        Assert.Equal(pinnedBinaryOutput, binaryOutput);
        Assert.Equal(pinnedBinaryError, binaryError);
    }

    /// <summary>
    /// Verifies binary-file messages use path prefixes even when null path mode is enabled.
    /// </summary>
    [Fact]
    public void BinaryFileMessageUsesColonPathPrefixInNullMode()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("alpha\0needle\nnext needle\n"));

        (int exitCode, byte[] output, string error) = RunScout("-0", "-H", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-0", "-H", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies count mode treats NUL bytes as line breaks for binary files.
    /// </summary>
    [Fact]
    public void BinaryFileCountModeUsesConvertedBinaryLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0bbb\n"));

        (int exitCode, byte[] output, string error) = RunScout("-c", "-v", "zzz", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-c", "-v", "zzz", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies files-without-match treats binary-quit files like ripgrep.
    /// </summary>
    [Fact]
    public void BinaryFileFilesWithoutMatchStopsAtFirstNul()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0needle\n"));

        (int exitCode, byte[] output, string error) = RunScout("--files-without-match", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--files-without-match", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies implicit large binary count mode suppresses output like ripgrep.
    /// </summary>
    [Fact]
    public void LargeImplicitBinaryCountSuppressesOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "-c", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "-c", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large binary count mode converts NUL bytes to line feeds like ripgrep.
    /// </summary>
    [Fact]
    public void LargeBinaryCountModeUsesConvertedBinaryLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--binary", "-c", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--binary", "-c", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies implicit large binary count-matches mode suppresses output like ripgrep.
    /// </summary>
    [Fact]
    public void LargeImplicitBinaryCountMatchesSuppressesOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--count-matches", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--count-matches", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large binary count-matches mode converts NUL bytes to line feeds like ripgrep.
    /// </summary>
    [Fact]
    public void LargeBinaryCountMatchesModeUsesConvertedBinaryLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--binary", "--count-matches", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--binary", "--count-matches", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large count-matches mode applies max-count to matching lines across stream segments.
    /// </summary>
    [Fact]
    public void LargeCountMatchesMaxCountLimitsMatchingLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, CreateLargeCountMatchesMaxCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--count-matches", "-m", "3", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--count-matches", "-m", "3", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies the large-file fast literal line-number path matches ripgrep across block boundaries.
    /// </summary>
    [Fact]
    public void LargeFastLiteralLineNumbersMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, CreateLargeFastLiteralLineNumberInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--binary", "--no-filename", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--binary", "--no-filename", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies the large-file fast literal path preserves safe-prefix output before a late NUL.
    /// </summary>
    [Fact]
    public void LargeFastLiteralBinaryFallbackMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeFastLiteralBinaryInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--binary", "--no-filename", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--binary", "--no-filename", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies implicit large binary standard search stops at ripgrep's binary-safe prefix.
    /// </summary>
    [Fact]
    public void LargeImplicitBinaryStandardStopsAtFirstNul()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryCountInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--sort=path", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--sort=path", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large binary standard search reports binary matches after converted NUL bytes.
    /// </summary>
    [Fact]
    public void LargeBinaryStandardReportsBinaryMatch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryMatchAfterNulInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--binary", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--binary", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies large files-without-match mode treats binary-quit files like ripgrep.
    /// </summary>
    [Fact]
    public void LargeBinaryFilesWithoutMatchStopsAtFirstNul()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, CreateLargeBinaryMatchAfterNulInput());

        (int exitCode, byte[] output, string error) = RunScout("--no-mmap", "--files-without-match", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-mmap", "--files-without-match", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies context mode stops before matches that appear after the first NUL byte.
    /// </summary>
    [Fact]
    public void BinaryFileContextModeStopsAtFirstNul()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("aaa\0bbb\n"));

        (int exitCode, byte[] output, string error) = RunScout("-C1", "bbb", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-C1", "bbb", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies binary-file messages are not rewritten as heading output.
    /// </summary>
    [Fact]
    public void BinaryFileHeadingModePrintsInlineBinaryMessage()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("alpha\0needle\nnext needle\n"));

        (int exitCode, byte[] output, string error) = RunScout("--heading", "-H", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--heading", "-H", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch stops output after the first non-matching line following matches.
    /// </summary>
    [Fact]
    public void StopOnNonmatchModesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nneedle two\nbeta\nneedle three\n");

        (int contextExitCode, byte[] contextOutput, string contextError) = RunScout("--stop-on-nonmatch", "-n", "-A2", "needle", path);
        (int pinnedContextExitCode, byte[] pinnedContextOutput, string pinnedContextError) = RunPinnedRipgrep("--stop-on-nonmatch", "-n", "-A2", "needle", path);
        (int countExitCode, byte[] countOutput, string countError) = RunScout("--stop-on-nonmatch", "-c", "needle", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("--stop-on-nonmatch", "-c", "needle", path);
        (int countMatchesExitCode, byte[] countMatchesOutput, string countMatchesError) = RunScout("--stop-on-nonmatch", "--count-matches", "needle", path);
        (int pinnedCountMatchesExitCode, byte[] pinnedCountMatchesOutput, string pinnedCountMatchesError) = RunPinnedRipgrep("--stop-on-nonmatch", "--count-matches", "needle", path);
        (int passthruExitCode, byte[] passthruOutput, string passthruError) = RunScout("--stop-on-nonmatch", "--passthru", "-n", "needle", path);
        (int pinnedPassthruExitCode, byte[] pinnedPassthruOutput, string pinnedPassthruError) = RunPinnedRipgrep("--stop-on-nonmatch", "--passthru", "-n", "needle", path);

        Assert.Equal(pinnedContextExitCode, contextExitCode);
        Assert.Equal(pinnedContextOutput, contextOutput);
        Assert.Equal(pinnedContextError, contextError);
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
        Assert.Equal(pinnedCountMatchesExitCode, countMatchesExitCode);
        Assert.Equal(pinnedCountMatchesOutput, countMatchesOutput);
        Assert.Equal(pinnedCountMatchesError, countMatchesError);
        Assert.Equal(pinnedPassthruExitCode, passthruExitCode);
        Assert.Equal(pinnedPassthruOutput, passthruOutput);
        Assert.Equal(pinnedPassthruError, passthruError);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch uses selected lines after invert-match is applied.
    /// </summary>
    [Fact]
    public void StopOnNonmatchInvertMatchMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nneedle two\nbeta\n");

        (int exitCode, byte[] output, string error) = RunScout("--stop-on-nonmatch", "-v", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--stop-on-nonmatch", "-v", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies automatic BOM sniffing matches pinned ripgrep for UTF-8 and UTF-16 inputs.
    /// </summary>
    [Fact]
    public void EncodingBomSniffingMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string utf8 = Path.Combine(root, "utf8.txt");
        string utf16Le = Path.Combine(root, "utf16le.txt");
        string utf16Be = Path.Combine(root, "utf16be.txt");
        File.WriteAllBytes(utf8, [0xEF, 0xBB, 0xBF, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e', (byte)'\n']);
        File.WriteAllBytes(utf16Le, [0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);
        File.WriteAllBytes(utf16Be, [0xFE, 0xFF, 0, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n']);

        (int utf8ExitCode, byte[] utf8Output, string utf8Error) = RunScout("-n", "--column", "-b", "needle", utf8);
        (int pinnedUtf8ExitCode, byte[] pinnedUtf8Output, string pinnedUtf8Error) = RunPinnedRipgrep("-n", "--column", "-b", "needle", utf8);
        (int leExitCode, byte[] leOutput, string leError) = RunScout("-n", "--column", "-b", "needle", utf16Le);
        (int pinnedLeExitCode, byte[] pinnedLeOutput, string pinnedLeError) = RunPinnedRipgrep("-n", "--column", "-b", "needle", utf16Le);
        (int beExitCode, byte[] beOutput, string beError) = RunScout("-n", "--column", "-b", "needle", utf16Be);
        (int pinnedBeExitCode, byte[] pinnedBeOutput, string pinnedBeError) = RunPinnedRipgrep("-n", "--column", "-b", "needle", utf16Be);

        Assert.Equal(pinnedUtf8ExitCode, utf8ExitCode);
        Assert.Equal(pinnedUtf8Output, utf8Output);
        Assert.Equal(pinnedUtf8Error, utf8Error);
        Assert.Equal(pinnedLeExitCode, leExitCode);
        Assert.Equal(pinnedLeOutput, leOutput);
        Assert.Equal(pinnedLeError, leError);
        Assert.Equal(pinnedBeExitCode, beExitCode);
        Assert.Equal(pinnedBeOutput, beOutput);
        Assert.Equal(pinnedBeError, beError);
    }

    /// <summary>
    /// Verifies explicit encoding flags and negation match pinned ripgrep.
    /// </summary>
    [Fact]
    public void EncodingFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string utf8Bom = Path.Combine(root, "utf8.txt");
        string utf16Le = Path.Combine(root, "utf16le.txt");
        string utf16Be = Path.Combine(root, "utf16be.txt");
        File.WriteAllBytes(utf8Bom, [0xEF, 0xBB, 0xBF, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e', (byte)'\n']);
        File.WriteAllBytes(utf16Le, [(byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);
        File.WriteAllBytes(utf16Be, [0, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n']);

        (int noneExitCode, byte[] noneOutput, string noneError) = RunScout("--encoding", "none", "needle", utf8Bom);
        (int pinnedNoneExitCode, byte[] pinnedNoneOutput, string pinnedNoneError) = RunPinnedRipgrep("--encoding", "none", "needle", utf8Bom);
        (int resetExitCode, byte[] resetOutput, string resetError) = RunScout("-E", "none", "--no-encoding", "needle", utf8Bom);
        (int pinnedResetExitCode, byte[] pinnedResetOutput, string pinnedResetError) = RunPinnedRipgrep("-E", "none", "--no-encoding", "needle", utf8Bom);
        (int leExitCode, byte[] leOutput, string leError) = RunScout("-E", "utf-16", "needle", utf16Le);
        (int pinnedLeExitCode, byte[] pinnedLeOutput, string pinnedLeError) = RunPinnedRipgrep("-E", "utf-16", "needle", utf16Le);
        (int beExitCode, byte[] beOutput, string beError) = RunScout("-E", "utf-16be", "needle", utf16Be);
        (int pinnedBeExitCode, byte[] pinnedBeOutput, string pinnedBeError) = RunPinnedRipgrep("-E", "utf-16be", "needle", utf16Be);

        Assert.Equal(pinnedNoneExitCode, noneExitCode);
        Assert.Equal(pinnedNoneOutput, noneOutput);
        Assert.Equal(pinnedNoneError, noneError);
        Assert.Equal(pinnedResetExitCode, resetExitCode);
        Assert.Equal(pinnedResetOutput, resetOutput);
        Assert.Equal(pinnedResetError, resetError);
        Assert.Equal(pinnedLeExitCode, leExitCode);
        Assert.Equal(pinnedLeOutput, leOutput);
        Assert.Equal(pinnedLeError, leError);
        Assert.Equal(pinnedBeExitCode, beExitCode);
        Assert.Equal(pinnedBeOutput, beOutput);
        Assert.Equal(pinnedBeError, beError);
    }

    /// <summary>
    /// Verifies WHATWG single-byte encoding labels match pinned ripgrep behavior.
    /// </summary>
    [Fact]
    public void Windows1252EncodingLabelMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "windows1252.txt");
        File.WriteAllBytes(path, [(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n']);

        (int exitCode, byte[] output, string error) = RunScout("-E", "latin1", "caf\u00e9", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-E", "latin1", "caf\u00e9", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies ISO-8859-2 labels and decoding match pinned ripgrep behavior.
    /// </summary>
    [Fact]
    public void Iso88592EncodingLabelMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "iso88592.txt");
        File.WriteAllBytes(path, [0xA1, 0xB1, (byte)'\n']);

        (int exitCode, byte[] output, string error) = RunScout("-E", "latin2", "\u0104\u0105", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-E", "latin2", "\u0104\u0105", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies Windows-1251 labels and decoding match pinned ripgrep behavior.
    /// </summary>
    [Fact]
    public void Windows1251EncodingLabelMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "windows1251.txt");
        File.WriteAllBytes(path, [0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, (byte)'\n']);

        (int exitCode, byte[] output, string error) = RunScout("-E", "windows-1251", "\u041F\u0440\u0438\u0432\u0435\u0442", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-E", "windows-1251", "\u041F\u0440\u0438\u0432\u0435\u0442", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies additional encoding labels and decoding match pinned ripgrep behavior.
    /// </summary>
    [Fact]
    public void AdditionalEncodingLabelsMatchPinnedRipgrep()
    {
        AssertEncodingLabelMatchesPinnedRipgrep("euc-kr", "\uAC00\uB098\uB2E4", [0xB0, 0xA1, 0xB3, 0xAA, 0xB4, 0xD9, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("euc-kr", "\uFFFD0", [0xB0, (byte)'0', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-949", "\uAC02", [0x81, 0x41, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("euc-jp", "\u65E5\u672C\u8A9E", [0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("x-euc-jp", "\u4E02\u02D8", [0x8F, 0xB0, 0xA1, 0x8F, 0xA2, 0xAF, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("euc-jp", "\uFFFD0", [0x8F, 0xB0, (byte)'0', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("big5", "\u4E2D\u6587", [0xA4, 0xA4, 0xA4, 0xE5, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("big5-hkscs", "\U00027267", [0x87, 0x45, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("big5", "\u00CA\u0304", [0x88, 0x62, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("gbk", "\u20AC\u4F60\u597D", [0x80, 0xC4, 0xE3, 0xBA, 0xC3, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("gb2312", "\u4F60\u597D", [0xC4, 0xE3, 0xBA, 0xC3, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("gb18030", "\uD83D\uDE00", [0x94, 0x39, 0xFC, 0x36, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("gb18030", "\uFFFDA", [0x84, 0x39, 0x81, 0x30, (byte)'A', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("shift-jis", "\u65E5\u672C\u8A9E", [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-31j", "\u3042\u30A2\uFF76", [0x82, 0xA0, 0x83, 0x41, 0xB6, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("shift-jis", "\uFFFD0", [0x82, (byte)'0', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("iso-2022-jp", "\u65E5\u672C\u8A9E", [0x1B, (byte)'$', (byte)'B', 0x46, 0x7C, 0x4B, 0x5C, 0x38, 0x6C, 0x1B, (byte)'(', (byte)'B', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("csiso2022jp", "\u00A5\u203E\uFF76", [0x1B, (byte)'(', (byte)'J', (byte)'\\', (byte)'~', 0x1B, (byte)'(', (byte)'I', (byte)'6', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("iso-2022-jp", "\uFFFDxA", [0x1B, (byte)'x', (byte)'A', (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("ibm866", "\u041F\u043F\u0440\u0438\u0432\u0435\u0442", [0x8F, 0xAF, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("latin3", "\u0126\u0127", [0xA1, 0xB1, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("latin4", "\u0104\u0105", [0xA1, 0xB1, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("cyrillic", "\u0410\u0430", [0xB0, 0xD0, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("arabic", "\u0627\u0643", [0xC7, 0xE3, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("greek", "\u0391\u03B1", [0xC1, 0xE1, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("hebrew", "\u05D0\u05EA", [0xE0, 0xFA, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("logical", "\u05D0\u05EA", [0xE0, 0xFA, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("latin6", "\u0104\u0105", [0xA1, 0xB1, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("iso-8859-13", "\u201D\u201C", [0xA1, 0xB4, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("iso-8859-14", "\u1E02\u1E03", [0xA1, 0xA2, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("l9", "\u20AC\u0152", [0xA4, 0xBC, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("iso-8859-16", "\u0104\u0105", [0xA1, 0xA2, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("koi8-r", "\u041F\u0440\u0438\u0432\u0435\u0442", [0xF0, 0xD2, 0xC9, 0xD7, 0xC5, 0xD4, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("koi8-u", "\u0454\u0404", [0xA4, 0xB4, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("macintosh", "\u00C4\u00E9", [0x80, 0x8E, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-874", "\u0E01\u0E3F", [0xA1, 0xDF, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1250", "\u015A\u015B", [0x8C, 0x9C, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1253", "\u0391\u03B1", [0xC1, 0xE1, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1254", "\u011E\u0130\u0131\u015F", [0xD0, 0xDD, 0xFD, 0xFE, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1255", "\u05D0\u05EA", [0xE0, 0xFA, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1256", "\u0627\u0645\u064A", [0xC7, 0xE3, 0xED, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1257", "\u0104\u0105", [0xC0, 0xE0, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("windows-1258", "\u0300\u20AB", [0xCC, 0xFE, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("x-mac-cyrillic", "\u0410\u044F", [0x80, 0xDF, (byte)'\n']);
        AssertEncodingLabelMatchesPinnedRipgrep("x-user-defined", "\uF780\uF7FF", [0x80, 0xFF, (byte)'\n']);
    }

    private static void AssertEncodingLabelMatchesPinnedRipgrep(string label, string pattern, byte[] contents)
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, label.Replace('-', '_') + ".txt");
        File.WriteAllBytes(path, contents);

        (int exitCode, byte[] output, string error) = RunScout("-E", label, pattern, path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-E", label, pattern, path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies JSON output reports decoded byte offsets after BOM sniffing.
    /// </summary>
    [Fact]
    public void EncodingJsonMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, [0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0]);

        (int exitCode, byte[] output, string error) = RunScout("--json", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies CRLF line-regexp matching follows the pinned ripgrep line terminator mode.
    /// </summary>
    [Fact]
    public void CrlfLineRegexpModesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, "needle\nneedle\r\nmiss\r\n"u8.ToArray());

        (int defaultExitCode, byte[] defaultOutput, string defaultError) = RunScout("-x", "-n", "needle", path);
        (int pinnedDefaultExitCode, byte[] pinnedDefaultOutput, string pinnedDefaultError) = RunPinnedRipgrep("-x", "-n", "needle", path);
        (int crlfExitCode, byte[] crlfOutput, string crlfError) = RunScout("--crlf", "-x", "-n", "needle", path);
        (int pinnedCrlfExitCode, byte[] pinnedCrlfOutput, string pinnedCrlfError) = RunPinnedRipgrep("--crlf", "-x", "-n", "needle", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("--crlf", "--no-crlf", "-x", "-n", "needle", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("--crlf", "--no-crlf", "-x", "-n", "needle", path);

        Assert.Equal(pinnedDefaultExitCode, defaultExitCode);
        Assert.Equal(pinnedDefaultOutput, defaultOutput);
        Assert.Equal(pinnedDefaultError, defaultError);
        Assert.Equal(pinnedCrlfExitCode, crlfExitCode);
        Assert.Equal(pinnedCrlfOutput, crlfOutput);
        Assert.Equal(pinnedCrlfError, crlfError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
    }

    /// <summary>
    /// Verifies CRLF mode uses CRLF for generated search-mode line terminators.
    /// </summary>
    [Fact]
    public void CrlfGeneratedTerminatorsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, "needle"u8.ToArray());

        (int countExitCode, byte[] countOutput, string countError) = RunScout("--crlf", "-c", "needle", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("--crlf", "-c", "needle", path);
        (int filesExitCode, byte[] filesOutput, string filesError) = RunScout("--crlf", "-l", "needle", path);
        (int pinnedFilesExitCode, byte[] pinnedFilesOutput, string pinnedFilesError) = RunPinnedRipgrep("--crlf", "-l", "needle", path);

        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
        Assert.Equal(pinnedFilesExitCode, filesExitCode);
        Assert.Equal(pinnedFilesOutput, filesOutput);
        Assert.Equal(pinnedFilesError, filesError);
    }

    /// <summary>
    /// Verifies CRLF line-regexp JSON submatch spans exclude the CRLF terminator.
    /// </summary>
    [Fact]
    public void CrlfJsonSubmatchesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, "needle\r\n"u8.ToArray());

        (int exitCode, byte[] output, string error) = RunScout("--json", "--crlf", "-x", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "--crlf", "-x", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies NUL data mode uses NUL-separated records across common search modes.
    /// </summary>
    [Fact]
    public void NullDataSearchModesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, "needle\0miss\0needle"u8.ToArray());

        (int standardExitCode, byte[] standardOutput, string standardError) = RunScout("--null-data", "needle", path);
        (int pinnedStandardExitCode, byte[] pinnedStandardOutput, string pinnedStandardError) = RunPinnedRipgrep("--null-data", "needle", path);
        (int lineNumberExitCode, byte[] lineNumberOutput, string lineNumberError) = RunScout("--null-data", "-n", "needle", path);
        (int pinnedLineNumberExitCode, byte[] pinnedLineNumberOutput, string pinnedLineNumberError) = RunPinnedRipgrep("--null-data", "-n", "needle", path);
        (int lineRegexpExitCode, byte[] lineRegexpOutput, string lineRegexpError) = RunScout("--null-data", "-x", "needle", path);
        (int pinnedLineRegexpExitCode, byte[] pinnedLineRegexpOutput, string pinnedLineRegexpError) = RunPinnedRipgrep("--null-data", "-x", "needle", path);
        (int onlyMatchingExitCode, byte[] onlyMatchingOutput, string onlyMatchingError) = RunScout("--null-data", "-o", "needle", path);
        (int pinnedOnlyMatchingExitCode, byte[] pinnedOnlyMatchingOutput, string pinnedOnlyMatchingError) = RunPinnedRipgrep("--null-data", "-o", "needle", path);
        (int countExitCode, byte[] countOutput, string countError) = RunScout("--null-data", "-c", "needle", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("--null-data", "-c", "needle", path);
        (int filesExitCode, byte[] filesOutput, string filesError) = RunScout("--null-data", "-l", "needle", path);
        (int pinnedFilesExitCode, byte[] pinnedFilesOutput, string pinnedFilesError) = RunPinnedRipgrep("--null-data", "-l", "needle", path);

        Assert.Equal(pinnedStandardExitCode, standardExitCode);
        Assert.Equal(pinnedStandardOutput, standardOutput);
        Assert.Equal(pinnedStandardError, standardError);
        Assert.Equal(pinnedLineNumberExitCode, lineNumberExitCode);
        Assert.Equal(pinnedLineNumberOutput, lineNumberOutput);
        Assert.Equal(pinnedLineNumberError, lineNumberError);
        Assert.Equal(pinnedLineRegexpExitCode, lineRegexpExitCode);
        Assert.Equal(pinnedLineRegexpOutput, lineRegexpOutput);
        Assert.Equal(pinnedLineRegexpError, lineRegexpError);
        Assert.Equal(pinnedOnlyMatchingExitCode, onlyMatchingExitCode);
        Assert.Equal(pinnedOnlyMatchingOutput, onlyMatchingOutput);
        Assert.Equal(pinnedOnlyMatchingError, onlyMatchingError);
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
        Assert.Equal(pinnedFilesExitCode, filesExitCode);
        Assert.Equal(pinnedFilesOutput, filesOutput);
        Assert.Equal(pinnedFilesError, filesError);
    }

    /// <summary>
    /// Verifies NUL data mode treats LF as data while files mode keeps path line feeds.
    /// </summary>
    [Fact]
    public void NullDataTreatsLfAsDataAndFilesModeUsesLf()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllBytes(path, "needle\nmiss\n"u8.ToArray());

        (int searchExitCode, byte[] searchOutput, string searchError) = RunScout("--null-data", "needle", path);
        (int pinnedSearchExitCode, byte[] pinnedSearchOutput, string pinnedSearchError) = RunPinnedRipgrep("--null-data", "needle", path);
        (int filesExitCode, byte[] filesOutput, string filesError) = RunScout("--null-data", "--files", root);
        (int pinnedFilesExitCode, byte[] pinnedFilesOutput, string pinnedFilesError) = RunPinnedRipgrep("--null-data", "--files", root);

        Assert.Equal(pinnedSearchExitCode, searchExitCode);
        Assert.Equal(pinnedSearchOutput, searchOutput);
        Assert.Equal(pinnedSearchError, searchError);
        Assert.Equal(pinnedFilesExitCode, filesExitCode);
        Assert.Equal(pinnedFilesOutput, filesOutput);
        Assert.Equal(pinnedFilesError, filesError);
    }

    /// <summary>
    /// Verifies NUL data JSON output reports text records instead of binary offsets.
    /// </summary>
    [Fact]
    public void NullDataJsonMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, "needle\0miss\0needle"u8.ToArray());

        (int exitCode, byte[] output, string error) = RunScout("--json", "--null-data", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "--null-data", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies CRLF and NUL data modes use ripgrep's positive-flag last-wins behavior.
    /// </summary>
    [Fact]
    public void NullDataAndCrlfLastWinsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.dat");
        File.WriteAllBytes(path, "needle\0needle\r\nneedle"u8.ToArray());

        (int nullDataExitCode, byte[] nullDataOutput, string nullDataError) = RunScout("--crlf", "--null-data", "-x", "needle", path);
        (int pinnedNullDataExitCode, byte[] pinnedNullDataOutput, string pinnedNullDataError) = RunPinnedRipgrep("--crlf", "--null-data", "-x", "needle", path);
        (int crlfExitCode, byte[] crlfOutput, string crlfError) = RunScout("--null-data", "--crlf", "-x", "needle", path);
        (int pinnedCrlfExitCode, byte[] pinnedCrlfOutput, string pinnedCrlfError) = RunPinnedRipgrep("--null-data", "--crlf", "-x", "needle", path);

        Assert.Equal(pinnedNullDataExitCode, nullDataExitCode);
        Assert.Equal(pinnedNullDataOutput, nullDataOutput);
        Assert.Equal(pinnedNullDataError, nullDataError);
        Assert.Equal(pinnedCrlfExitCode, crlfExitCode);
        Assert.Equal(pinnedCrlfOutput, crlfOutput);
        Assert.Equal(pinnedCrlfError, crlfError);
    }
    private static (int ExitCode, byte[] Output, string Error) RunScout(params string[] arguments)
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        var osArguments = new OsString[arguments.Length + 1];
        osArguments[0] = OsString.FromUnixBytes("scout"u8);
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index + 1] = OsString.FromText(arguments[index]);
        }

        string? previousSourceRoot = Environment.GetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT");
        Environment.SetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT", PinnedRipgrepOracle.ReferenceRoot);
        int exitCode;
        try
        {
            exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter, configPath: null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT", previousSourceRoot);
        }

        return (exitCode, output.ToArray(), Utf8(error.ToArray()));
    }

    private static (int ExitCode, byte[] Output, string Error) RunScoutWithConfig(string configPath, params string[] arguments)
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        var osArguments = new OsString[arguments.Length + 1];
        osArguments[0] = OsString.FromUnixBytes("scout"u8);
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index + 1] = OsString.FromText(arguments[index]);
        }

        string? previousSourceRoot = Environment.GetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT");
        Environment.SetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT", PinnedRipgrepOracle.ReferenceRoot);
        int exitCode;
        try
        {
            exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter, configPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCOUT_RIPGREP_SOURCE_ROOT", previousSourceRoot);
        }

        return (exitCode, output.ToArray(), Utf8(error.ToArray()));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] CreateLargeBinaryCountInput()
    {
        using MemoryStream stream = new();
        byte[] before = Encoding.UTF8.GetBytes("needle before\n");
        byte[] binary = Encoding.UTF8.GetBytes("alpha\0needle after\n");
        byte[] after = Encoding.UTF8.GetBytes("needle tail\n");
        for (int index = 0; index < 25_000; index++)
        {
            stream.Write(before);
        }

        stream.Write(binary);
        for (int index = 0; index < 25_000; index++)
        {
            stream.Write(after);
        }

        return stream.ToArray();
    }

    private static byte[] CreateLargeBinaryMatchAfterNulInput()
    {
        using MemoryStream stream = new();
        byte[] before = Encoding.UTF8.GetBytes("alpha before\n");
        byte[] binary = Encoding.UTF8.GetBytes("alpha\0needle after\n");
        for (int index = 0; index < 25_000; index++)
        {
            stream.Write(before);
        }

        stream.Write(binary);
        return stream.ToArray();
    }

    private static byte[] CreateLargeCountMatchesMaxCountInput()
    {
        using MemoryStream stream = new();
        stream.Write("needle needle "u8);
        stream.Write(new byte[300_000]);
        stream.Write("\nneedle needle\nneedle needle\nneedle needle\n"u8);
        return stream.ToArray();
    }

    private static byte[] CreateLargeFastLiteralLineNumberInput()
    {
        using MemoryStream stream = new();
        stream.Write("needle first\n"u8);
        WriteRepeated(stream, (byte)'x', 70_000);
        stream.Write("\nneedle second\n"u8);
        WriteRepeated(stream, (byte)'y', 70_000);
        stream.Write("\ntail needle"u8);
        return stream.ToArray();
    }

    private static byte[] CreateLargeFastLiteralBinaryInput()
    {
        using MemoryStream stream = new();
        byte[] safeLine = "needle before\n"u8.ToArray();
        for (int index = 0; index < 6_000; index++)
        {
            stream.Write(safeLine);
        }

        stream.Write("alpha\0needle after\n"u8);
        return stream.ToArray();
    }

    private static void WriteRepeated(MemoryStream stream, byte value, int count)
    {
        byte[] buffer = new byte[Math.Min(count, 4096)];
        Array.Fill(buffer, value);
        while (count > 0)
        {
            int length = Math.Min(count, buffer.Length);
            stream.Write(buffer.AsSpan(0, length));
            count -= length;
        }
    }

    private static void WriteGzipFile(string path, string contents)
    {
        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        gzip.Write(bytes);
    }

    private static void WriteCompressedFile(string path, string program, string[] arguments, byte[] contents)
    {
        if (OperatingSystem.IsWindows() && string.Equals(program, "compress", StringComparison.Ordinal))
        {
            program = "gzip";
            arguments = ["-c"];
        }

        bool useInputFile = string.Equals(program, "compress", StringComparison.Ordinal);
        useInputFile |= OperatingSystem.IsWindows() && string.Equals(program, "gzip", StringComparison.Ordinal) && arguments is ["-c"];
        string? inputPath = null;
        ProcessStartInfo startInfo = new(program)
        {
            RedirectStandardError = true,
            RedirectStandardInput = !useInputFile,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        if (useInputFile)
        {
            inputPath = path + ".input";
            File.WriteAllBytes(inputPath, contents);
            startInfo.ArgumentList.Add(inputPath);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        try
        {
            Assert.True(process.Start());
            if (!useInputFile)
            {
                process.StandardInput.BaseStream.Write(contents);
                process.StandardInput.Close();
            }

            using MemoryStream output = new();
            process.StandardOutput.BaseStream.CopyTo(output);
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"{program} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error}");
            File.WriteAllBytes(path, output.ToArray());
        }
        finally
        {
            if (inputPath is not null)
            {
                File.Delete(inputPath);
            }
        }
    }

    private static string CreatePreprocessorScript(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsPreprocessorScript(root, fail: false);
        }

        return CreateUnixPreprocessorScript(root, "cat >/dev/null\nprintf 'needle from preprocessor\n'\n");
    }

    private static string CreateFailingPreprocessorScript(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsPreprocessorScript(root, fail: true);
        }

        return CreateUnixPreprocessorScript(root, "cat >/dev/null\nprintf 'prefail\\n' >&2\nexit 7\n");
    }

    private static string CreateUnixPreprocessorScript(string root, string body)
    {
        string path = Path.Combine(root, "preprocessor.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body);

        ProcessStartInfo startInfo = new("chmod")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("+x");
        startInfo.ArgumentList.Add(path);

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return path;
    }

    private static string CreateWindowsPreprocessorScript(string root, bool fail)
    {
        string path = Path.Combine(root, "preprocessor.cmd");
        string body = fail
            ? "@echo off\r\nmore >NUL\r\necho prefail 1>&2\r\nexit /B 7\r\n"
            : "@echo off\r\nmore >NUL\r\necho needle from preprocessor\r\n";
        File.WriteAllText(path, body);
        return path;
    }

    private static bool TryCreateDirectorySymlink(string target, string link)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Utf8(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ScoutSource(string sourcePath)
    {
        return OperatingSystem.IsWindows()
            ? sourcePath.Replace('/', '\\')
            : sourcePath.Replace('\\', '/');
    }

    private static string[] SortedUtf8Lines(byte[] bytes)
    {
        string[] lines = Utf8(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Array.Sort(lines, StringComparer.Ordinal);
        return lines;
    }

    private static byte[] ReadPinnedRipgrepOutput(string argument)
    {
        (int exitCode, byte[] output, string error) = RunPinnedRipgrep(argument);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        return output;
    }

    private static string NormalizeJsonTimings(byte[] output)
    {
        string[] lines = Utf8(output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        for (int index = 0; index < lines.Length; index++)
        {
            JsonNode node = JsonNode.Parse(lines[index])!;
            NormalizeJsonTimingNode(node);
            builder.Append(node.ToJsonString());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string SortedNormalizedJsonMessages(byte[] output)
    {
        string[] lines = NormalizeJsonTimings(output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Array.Sort(lines, StringComparer.Ordinal);
        var builder = new StringBuilder();
        for (int index = 0; index < lines.Length; index++)
        {
            builder.Append(lines[index]);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string NormalizeStatsTimings(byte[] output)
    {
        string[] lines = Utf8(output).Split('\n');
        var builder = new StringBuilder();
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.EndsWith(" seconds spent searching", StringComparison.Ordinal))
            {
                line = "0.000000 seconds spent searching";
            }
            else if (line.EndsWith(" seconds total", StringComparison.Ordinal))
            {
                line = "0.000000 seconds total";
            }

            builder.Append(line);
            if (index + 1 < lines.Length)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void NormalizeJsonTimingNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject.ContainsKey("human") && jsonObject.ContainsKey("nanos") && jsonObject.ContainsKey("secs"))
            {
                jsonObject["human"] = "0.000000s";
                jsonObject["nanos"] = 0;
                jsonObject["secs"] = 0;
                return;
            }

            var children = new List<JsonNode>();
            foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
            {
                if (property.Value is not null)
                {
                    children.Add(property.Value);
                }
            }

            for (int index = 0; index < children.Count; index++)
            {
                NormalizeJsonTimingNode(children[index]);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            for (int index = 0; index < jsonArray.Count; index++)
            {
                if (jsonArray[index] is JsonNode child)
                {
                    NormalizeJsonTimingNode(child);
                }
            }
        }
    }

    private static (int ExitCode, byte[] Output, string Error) RunPinnedRipgrep(params string[] arguments)
    {
        ProcessStartInfo startInfo = PinnedRipgrepOracle.CreateStartInfo();
        startInfo.Environment.Remove("RIPGREP_CONFIG_PATH");
        startInfo.Environment.Remove("SCOUT_CONFIG_PATH");
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output.ToArray(), NormalizePinnedRipgrepStderrForScout(error));
    }

    private static (int ExitCode, byte[] Output, string Error) RunPinnedRipgrepWithConfig(string configPath, params string[] arguments)
    {
        ProcessStartInfo startInfo = PinnedRipgrepOracle.CreateStartInfo();
        startInfo.Environment.Remove("SCOUT_CONFIG_PATH");
        startInfo.Environment["RIPGREP_CONFIG_PATH"] = configPath;
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output.ToArray(), NormalizePinnedRipgrepStderrForScout(error));
    }

    private static string NormalizePinnedRipgrepStderrForScout(string error)
    {
        const string RipgrepConfigPathPlaceholder = "__SCOUT_RIPGREP_CONFIG_PATH__";
        string normalized = error.Replace(
            "SCOUT_CONFIG_PATH and RIPGREP_CONFIG_PATH environment variables are not set, therefore not reading any config file",
            "SCOUT_CONFIG_PATH and " + RipgrepConfigPathPlaceholder + " environment variables are not set, therefore not reading any config file",
            StringComparison.Ordinal);
        normalized = normalized.Replace(
            "RIPGREP_CONFIG_PATH environment variable is not set, therefore not reading any config file",
            "SCOUT_CONFIG_PATH and " + RipgrepConfigPathPlaceholder + " environment variables are not set, therefore not reading any config file",
            StringComparison.Ordinal);
        normalized = normalized.Replace("RIPGREP_CONFIG_PATH", "SCOUT_CONFIG_PATH", StringComparison.Ordinal);
        normalized = normalized.Replace(RipgrepConfigPathPlaceholder, "RIPGREP_CONFIG_PATH", StringComparison.Ordinal);
        normalized = normalized.Replace("rg:", "scout:", StringComparison.Ordinal);
        normalized = normalized.Replace("ripgrep requires at least one pattern to execute a search", "scout requires at least one pattern to execute a search", StringComparison.Ordinal);
        normalized = normalized.Replace("this build of ripgrep", "this build of scout", StringComparison.Ordinal);
        return normalized;
    }
}
