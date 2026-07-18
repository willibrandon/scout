namespace Scout;

/// <summary>
/// Verifies context consumers replay authoritative match spans with ripgrep semantics.
/// </summary>
public sealed class ContextAuthoritativeReplayDifferentialTests
{
    /// <summary>
    /// Verifies context, match, replacement, vimgrep, color, inversion, and limit consumers.
    /// </summary>
    [Fact]
    public void ContextConsumersReplayAuthoritativeSpans()
    {
        using var directory = RgTestDirectory.Create(
            "context-authoritative-replay");
        directory.CreateFile(
            "haystack.txt",
            "before\nab12 ------- ab34 ------- ab56\nafter\nmiss\nzz\ntail\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");
        string[] expressions =
            ["(?<word>ab)(?<digits>[0-9]+)", "(?<zed>z+)"];
        DifferentialCase[] cases =
        [
            DifferentialCase.Exact(CreateExpressionArguments(
                ["-n", "--column", "--byte-offset", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["-n", "--column", "--byte-offset", "-o", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["-n", "--column", "--byte-offset", "-r", "${digits}-${word}", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["--vimgrep", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["--vimgrep", "-C1", "-M12", "--max-columns-preview"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["--color=always", "-n", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["-v", "-n", "--column", "--byte-offset", "-C1"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["-m1", "-n", "-A2"],
                expressions,
                haystack)),
            DifferentialCase.Exact(CreateExpressionArguments(
                ["--passthru", "-m1", "-n"],
                expressions,
                haystack)),
            DifferentialCase.Exact("-n", "-o", "-C1", "(?<edge>^)", haystack),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(cases[index]);
        }
    }

    /// <summary>
    /// Verifies retained context spans preserve CRLF and NUL record semantics.
    /// </summary>
    [Fact]
    public void ContextRecordTerminatorsReplayAuthoritativeSpans()
    {
        using var directory = RgTestDirectory.Create(
            "context-authoritative-terminators");
        directory.CreateBytes(
            "crlf.txt",
            "before\r\nab12 ab34\r\nafter\r\n"u8.ToArray());
        directory.CreateBytes(
            "nul.bin",
            "before\0ab12 ab34\0after\0"u8.ToArray());
        string crlf = Path.Combine(directory.RootPath, "crlf.txt");
        string nul = Path.Combine(directory.RootPath, "nul.bin");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            "--crlf",
            "-n",
            "--column",
            "--byte-offset",
            "-C1",
            "ab[0-9]+",
            crlf));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            "--null-data",
            "-n",
            "--column",
            "--byte-offset",
            "-C1",
            "ab[0-9]+",
            nul));
    }

    /// <summary>
    /// Verifies host-stable physical-EOF replay and counting against ripgrep.
    /// </summary>
    [Fact]
    public void AbsoluteAnchorReplayMatchesPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create(
            "absolute-anchor-replay");
        directory.CreateBytes(
            "unterminated.txt",
            "a\nb"u8.ToArray());
        string unterminated = Path.Combine(
            directory.RootPath,
            "unterminated.txt");
        DifferentialCase[] cases =
        [
            DifferentialCase.Exact("-n", "-o", @"\z", unterminated),
            DifferentialCase.Exact("-n", "-C1", @"\z", unterminated),
            DifferentialCase.Exact("-n", "-v", "-C1", @"\z", unterminated),
            DifferentialCase.Exact("--count-matches", @"\z", unterminated),
            DifferentialCase.Exact("--count-matches", "$", unterminated),
        ];

        // The frozen osx-arm64 oracle rediscovers \A at every selected record, while the
        // frozen Windows and Linux oracles replay it against the original input prefix.
        // Focused Foundation tests pin Scout's architecture-independent full-prefix output.

        for (int index = 0; index < cases.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(cases[index]);
        }
    }

    /// <summary>
    /// Verifies vimgrep renders authoritative events across streaming, long-line, context, and only-matching output.
    /// </summary>
    [Fact]
    public void VimgrepRendersAuthoritativeMatchEvents()
    {
        using var directory = RgTestDirectory.Create(
            "vimgrep-authoritative-events");
        directory.CreateFile(
            "haystack.txt",
            "before\n   ab12 ---- ab34 ---- ab56\nafter\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");
        const string Pattern = "ab[0-9]+";
        DifferentialCase[] cases =
        [
            DifferentialCase.Exact(
                "--vimgrep",
                "--no-filename",
                Pattern,
                haystack),
            DifferentialCase.Exact(
                "--vimgrep",
                "--no-filename",
                "-M8",
                Pattern,
                haystack),
            DifferentialCase.Exact(
                "--vimgrep",
                "--no-filename",
                "--trim",
                "-M12",
                "--max-columns-preview",
                Pattern,
                haystack),
            DifferentialCase.Exact(
                "--vimgrep",
                "--no-filename",
                "-C1",
                "-M12",
                "--max-columns-preview",
                Pattern,
                haystack),
            DifferentialCase.Exact(
                "--vimgrep",
                "--no-filename",
                "--only-matching",
                "-M3",
                Pattern,
                haystack),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(cases[index]);
        }
    }

    private static string[] CreateExpressionArguments(
        string[] options,
        string[] patterns,
        params string[] paths)
    {
        var arguments = new List<string>(
            options.Length + (patterns.Length * 2) + paths.Length);
        arguments.AddRange(options);
        for (int index = 0; index < patterns.Length; index++)
        {
            arguments.Add("-e");
            arguments.Add(patterns[index]);
        }

        arguments.AddRange(paths);
        return arguments.ToArray();
    }
}
