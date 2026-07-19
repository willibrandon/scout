namespace Scout;

/// <summary>
/// Verifies combined ordered regex plans across repeated pattern sources and output consumers.
/// </summary>
public sealed class AuthoritativePatternSetDifferentialTests
{
    /// <summary>
    /// Verifies character-class intersections drive matching-line, match-count, and only-match output.
    /// </summary>
    [Fact]
    public void CharacterClassIntersectionConsumersMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-intersection");
        directory.CreateFile("haystack.txt", "abc\ndef\nzzz\nfeed\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", "[a-z&&def]+", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--count-matches", "[a-z&&def]+", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-o", "[a-z&&def]+", haystack));
    }

    /// <summary>
    /// Verifies the reported general-regex expressions agree for selected lines, counts, and spans.
    /// </summary>
    /// <param name="pattern">The reported regex expression.</param>
    /// <param name="contents">The input text.</param>
    [Theory]
    [InlineData(@"\bGeneratedRecord\b", "GeneratedRecords\nGeneratedRecord\nx GeneratedRecord y\n")]
    [InlineData(@"^internal sealed class GeneratedRecord\r?$", "other\r\ninternal sealed class GeneratedRecord\r\n")]
    [InlineData(@"^[A-Za-z_]{70,90}$", "short\nAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData(@"(?m)^Scout.*$", "other\nScout is authoritative\n")]
    [InlineData(@"(?:Generated|Paladin(?:Record|Value))", "Generated\nPaladinRecord\nPaladinValue\nPaladin\n")]
    [InlineData(@"(?:Absent|Missing(?:Two|Three))", "Absent\nMissingTwo\nMissingThree\nMissing\n")]
    [InlineData(@"[a-z--aeiou]+", "aeiou\nbcdf\nScout\n")]
    [InlineData(@"[a-f~~d-z]+", "abc\ndef\ngxyz\n")]
    [InlineData(@"[a-c[0-2]]+", "abc\n012\ndef\n")]
    [InlineData(@"\u{3B4}+", "δδ\nlambda λ\n")]
    [InlineData(@"\x{100}+", "ĀĀ\nA\n")]
    [InlineData(@"^t{1,2}+$", "t\ntt\nttt\ntttt\nx\n")]
    [InlineData(@"^Scout++$", "Scout\nScouttt\nScou\nScoutx\n")]
    [InlineData(@"^Scout{1,2}+$", "Scout\nScoutt\nScouttt\nScoutttt\nScoutx\n")]
    [InlineData(@"\p{Latin}+", "Latin\nΕλληνικά\n漢字\n")]
    [InlineData(@"[\w&&\p{Latin}]+", "Latin_123\nΕλληνικά\n漢字\n")]
    [InlineData(@"\p{Han}+", "Latin\nΕλληνικά\n漢字\n")]
    [InlineData(@"\p{sc=Latin}+", "abc\ń\nδ\n")]
    [InlineData(@"\p{scx=Latin}+", "abc\ń\nδ\n")]
    [InlineData(@"\P{Latin}+", "Latin\nΕλληνικά\n漢字\n")]
    [InlineData(@"[^\p{Latin}]+", "Latin\nΕλληνικά\n漢字\n")]
    [InlineData(@"\p{Zanb}+", "𑨀\nA\n")]
    public void ReportedGeneralRegexConsumersMatchPinnedRipgrep(string pattern, string contents)
    {
        using var directory = RgTestDirectory.Create("authoritative-reported-regex");
        directory.CreateFile("haystack.txt", contents);
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-n", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--count-matches", pattern, haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-o", pattern, haystack));
    }

    /// <summary>
    /// Verifies multiline analysis lowers non-ASCII scalar escapes without changing their matches.
    /// </summary>
    [Fact]
    public void MultilineScalarEscapeConsumersMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-multiline-scalar-escape");
        directory.CreateFile("haystack.txt", "δδ\nlambda λ\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-U", "-n", @"\u{3B4}+", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-U", "--count-matches", @"\u{3B4}+", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-U", "-o", @"\u{3B4}+", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("-U", "-i", "-o", @"\u{3B4}+", haystack));
    }

    /// <summary>
    /// Verifies automatic engine selection retains regex-syntax nested-repetition semantics.
    /// </summary>
    [Fact]
    public void AutoEngineChainedQuantifiersMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-auto-chained-quantifiers");
        directory.CreateFile("haystack.txt", "t\ntt\nttt\ntttt\nScout\nScouttt\nScoutttt\nScoutx\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");
        string[] patterns = [@"^t{1,2}+$", @"^Scout++$", @"^Scout{1,2}+$"];

        for (int index = 0; index < patterns.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                "--engine=auto",
                "-n",
                patterns[index],
                haystack));
            DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                "--engine=auto",
                "--count-matches",
                patterns[index],
                haystack));
        }
    }

    /// <summary>
    /// Verifies multiline and byte modes preserve character-class set algebra.
    /// </summary>
    [Fact]
    public void CharacterClassAlgebraModesMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-class-algebra-modes");
        directory.CreateFile("haystack.txt", "aeiou\nbcdf\nabc\n012\ndef\ngxyz\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");
        string[] patterns = ["[a-z--aeiou]+", "[a-f~~d-z]+", "[a-c[0-2]]+"];
        string[][] modes = [["-U"], ["--no-unicode"]];

        for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            for (int patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
            {
                DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                    [.. modes[modeIndex], "-n", patterns[patternIndex], haystack]));
                DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                    [.. modes[modeIndex], "--count-matches", patterns[patternIndex], haystack]));
                DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
                    [.. modes[modeIndex], "-o", patterns[patternIndex], haystack]));
            }
        }
    }

    /// <summary>
    /// Verifies fixed hexadecimal escapes retain one-byte semantics when Unicode mode is disabled.
    /// </summary>
    [Fact]
    public void ByteModeHexEscapeConsumersMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-byte-hex");
        directory.CreateBytes("haystack.bin", [0xFF, (byte)'\n', 0xC3, 0xBF, (byte)'\n']);
        string haystack = Path.Combine(directory.RootPath, "haystack.bin");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--no-unicode", "-n", @"\xFF", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--no-unicode", "--count-matches", @"\xFF", haystack));
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact("--no-unicode", "-o", @"\xFF", haystack));
    }

    /// <summary>
    /// Verifies repeated expression and pattern-file sources preserve ripgrep semantics as the set grows.
    /// </summary>
    /// <param name="patternCount">The number of ordered source patterns.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void RepeatedPatternSourcesMatchPinnedRipgrep(int patternCount)
    {
        using var directory = RgTestDirectory.Create("authoritative-pattern-set");
        directory.CreateFile("haystack.txt", "ab\nALPHA\nmiss\n");
        string haystack = Path.Combine(directory.RootPath, "haystack.txt");

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            CreateExpressionArguments(
                ["--count-matches"],
                CreatePatterns(patternCount, index => $"absent_{index}"),
                haystack)));

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            CreateExpressionArguments(
                ["-o"],
                CreatePatterns(
                    patternCount,
                    index => index switch
                    {
                        0 when patternCount == 1 => "ab|a",
                        0 => "ab",
                        1 => "a",
                        _ => $"absent_{index}",
                    }),
                haystack)));

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            CreateExpressionArguments(
                ["-n"],
                CreatePatterns(
                    patternCount,
                    index => index == 0 ? "(?i:alpha)" : $"absent_{index}"),
                haystack)));

        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            CreateExpressionArguments(
                ["--replace", "${capture0}"],
                CreatePatterns(
                    patternCount,
                    index => index == 0 ? "(?<capture0>a(?:b)?)" : $"(?<capture{index}>absent_{index})"),
                haystack)));

        string[] filePatterns = CreatePatterns(
            patternCount,
            index => index == 0
                ? "(?i:(?<value>alpha|ab))|absent"
                : $"absent_{index}");
        directory.CreateFile("patterns.txt", string.Join('\n', filePatterns) + "\n");
        DifferentialRunner.AssertMatchesPinned(DifferentialCase.Exact(
            "-n",
            "-f",
            Path.Combine(directory.RootPath, "patterns.txt"),
            haystack));
    }

    /// <summary>
    /// Verifies every line-oriented output consumer observes the same combined authoritative matcher.
    /// </summary>
    [Fact]
    public void CombinedPatternConsumersMatchPinnedRipgrep()
    {
        using var directory = RgTestDirectory.Create("authoritative-consumers");
        directory.CreateFile("first.txt", "alpha\nALPHA beta\ngamma\nmiss\nalphaalpha\n");
        directory.CreateFile("second.txt", "miss\nbeta\n");
        directory.CreateBytes("nul.bin", "alpha\0miss\0beta\0"u8.ToArray());
        directory.CreateFile("boundary.txt", new string('x', 131_060) + "\nalpha\n");

        string first = Path.Combine(directory.RootPath, "first.txt");
        string second = Path.Combine(directory.RootPath, "second.txt");
        string nul = Path.Combine(directory.RootPath, "nul.bin");
        string boundary = Path.Combine(directory.RootPath, "boundary.txt");
        string[] expressions = ["(?i:(?<word>alpha))", "beta", "^gamma$"];
        DifferentialCase[] cases =
        [
            DifferentialCase.Exact(CreateExpressionArguments([], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-c"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["--count-matches"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-o"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["--replace", "<$word>"], expressions, first)),
            DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, CreateExpressionArguments(["--json"], expressions, first)),
            DifferentialCase.Normalized(
                DifferentialComparisonMode.MaskElapsed,
                CreateExpressionArguments(["--json", "--replace", "<$word>"], expressions, first)),
            DifferentialCase.Normalized(
                DifferentialComparisonMode.MaskElapsed,
                CreateExpressionArguments(["--json", "-C1"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-l"], expressions, first, second)),
            DifferentialCase.Exact(CreateExpressionArguments(["-L"], expressions, first, second)),
            DifferentialCase.Exact(CreateExpressionArguments(["-q"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-n", "-C1"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-v"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-n", "--column"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-n", "--byte-offset"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["--vimgrep"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-n", "-M12", "--max-columns-preview"], expressions, first)),
            DifferentialCase.Normalized(
                DifferentialComparisonMode.MaskElapsed,
                CreateExpressionArguments(["--stats"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["-m1"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["--crlf", "-n"], expressions, first)),
            DifferentialCase.Exact(CreateExpressionArguments(["--null-data", "-n"], expressions, nul)),
            DifferentialCase.Exact(CreateExpressionArguments(["-j1", "--sort=path", "-nH"], expressions, directory.RootPath)),
            DifferentialCase.Normalized(
                DifferentialComparisonMode.SortLines,
                CreateExpressionArguments(["-j2", "-nH"], expressions, directory.RootPath)),
            DifferentialCase.Exact(CreateExpressionArguments(["-n"], expressions, boundary)),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            DifferentialRunner.AssertMatchesPinned(cases[index]);
        }
    }

    private static string[] CreatePatterns(int count, Func<int, string> createPattern)
    {
        string[] patterns = new string[count];
        for (int index = 0; index < patterns.Length; index++)
        {
            patterns[index] = createPattern(index);
        }

        return patterns;
    }

    private static string[] CreateExpressionArguments(
        string[] options,
        string[] patterns,
        params string[] paths)
    {
        var arguments = new List<string>(options.Length + (patterns.Length * 2) + paths.Length);
        for (int index = 0; index < options.Length; index++)
        {
            arguments.Add(options[index]);
        }

        for (int index = 0; index < patterns.Length; index++)
        {
            arguments.Add("-e");
            arguments.Add(patterns[index]);
        }

        arguments.AddRange(paths);
        return arguments.ToArray();
    }
}
