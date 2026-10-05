

namespace Scout;

/// <summary>
/// Verifies low-level CLI parsing behavior.
/// </summary>
[TestClass]
public sealed class CliParserTests
{
    /// <summary>
    /// Verifies the upstream option terminator preserves subsequent arguments as raw positional values.
    /// </summary>
    [TestMethod]
    public void OptionTerminatorPreservesPositionalArguments()
    {
        foreach (bool unix in new[] { true, false })
        {
            OsString separator = unix ? OsString.FromUnixBytes("--"u8) : OsString.FromWindowsString("--");
            var rawPath = OsString.FromUnixBytes([.. "-path-"u8, 0xFF]);
            CliParseResult result = CliParser.Parse([
                OsString.FromText("-n"), separator, OsString.FromText("--help"), rawPath, separator,
            ]);
            Assert.AreEqual(CliParseStatus.Ok, result.Status);
            Assert.IsNotNull(result.LowArgs);
            Assert.IsTrue(result.LowArgs.LineNumber);
            Assert.AreSequenceEqual(new[] { OsString.FromText("--help"), rawPath, separator }, result.LowArgs.Positional);
        }
    }

    /// <summary>
    /// Verifies <c>-V</c> selects the short version special mode.
    /// </summary>
    [TestMethod]
    public void ParsesShortVersionSpecialMode()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("-V"u8)]);

        Assert.AreEqual(CliParseStatus.Special, result.Status);
        Assert.AreEqual(CliSpecialMode.VersionShort, result.SpecialMode);
    }

    /// <summary>
    /// Verifies <c>--version</c> selects the long version special mode.
    /// </summary>
    [TestMethod]
    public void ParsesLongVersionSpecialMode()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("--version"u8)]);

        Assert.AreEqual(CliParseStatus.Special, result.Status);
        Assert.AreEqual(CliSpecialMode.VersionLong, result.SpecialMode);
    }

    /// <summary>
    /// Verifies positional arguments are retained as operating-system strings.
    /// </summary>
    [TestMethod]
    public void PreservesPositionalArguments()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes([0xff, 0x80])]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.ContainsSingle(result.LowArgs!.Positional);
        Assert.AreSequenceEqual<byte>([0xff, 0x80], result.LowArgs.Positional[0].AsUnixBytes().ToArray());
    }

    /// <summary>
    /// Verifies explicit regexp flags are parsed independently from positional paths.
    /// </summary>
    [TestMethod]
    public void ParsesRegexpFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("-e"u8), OsString.FromUnixBytes("needle"u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--regexp=alpha"u8), OsString.FromUnixBytes("-ebeta"u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult empty = CliParser.Parse(
            [OsString.FromUnixBytes("-e="u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--regexp"u8), OsString.FromUnixBytes("-needle"u8), OsString.FromUnixBytes("path.txt"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("needle"u8)], separate.LowArgs!.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("alpha"u8), OsString.FromUnixBytes("beta"u8)], inline.LowArgs!.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, empty.Status);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes(""u8)], empty.LowArgs!.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], empty.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, dashValue.Status);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("-needle"u8)], dashValue.LowArgs!.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies combined short flags are parsed with ripgrep-compatible value consumption.
    /// </summary>
    [TestMethod]
    public void ParsesCombinedShortFlags()
    {
        CliParseResult switches = CliParser.Parse(
            [OsString.FromUnixBytes("-nHiv"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inlineValue = CliParser.Parse(
            [OsString.FromUnixBytes("-nA2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult followingValue = CliParser.Parse(
            [OsString.FromUnixBytes("-ne"u8), OsString.FromUnixBytes("needle"u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult unrestricted = CliParser.Parse(
            [OsString.FromUnixBytes("-uuun"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult invalidValue = CliParser.Parse(
            [OsString.FromUnixBytes("-m1n"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult unknown = CliParser.Parse(
            [OsString.FromUnixBytes("-ny"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, switches.Status);
        Assert.IsTrue(switches.LowArgs!.LineNumber);
        Assert.IsTrue(switches.LowArgs.WithFilename);
        Assert.IsTrue(switches.LowArgs.InvertMatch);
        Assert.AreEqual(CliCaseMode.Insensitive, switches.LowArgs.CaseMode);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("needle"u8)], switches.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inlineValue.Status);
        Assert.IsTrue(inlineValue.LowArgs!.LineNumber);
        Assert.AreEqual(2UL, inlineValue.LowArgs.AfterContext);
        Assert.AreEqual(CliParseStatus.Ok, followingValue.Status);
        Assert.IsTrue(followingValue.LowArgs!.LineNumber);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("needle"u8)], followingValue.LowArgs.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], followingValue.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, unrestricted.Status);
        Assert.AreEqual(3, unrestricted.LowArgs!.UnrestrictedCount);
        Assert.IsTrue(unrestricted.LowArgs.LineNumber);
        Assert.AreEqual(CliParseStatus.Error, invalidValue.Status);
        Assert.AreEqual("error parsing flag -m: value is not a valid number: invalid digit found in string", invalidValue.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, unknown.Status);
        Assert.AreEqual("unrecognized flag -y", unknown.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies explicit pattern-file flags are parsed as ordered pattern sources.
    /// </summary>
    [TestMethod]
    public void ParsesPatternFileFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("-f"u8), OsString.FromUnixBytes("patterns.txt"u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--file=first.txt"u8), OsString.FromUnixBytes("-fsecond.txt"u8), OsString.FromUnixBytes("path.txt"u8)]);
        CliParseResult ordered = CliParser.Parse(
            [
                OsString.FromUnixBytes("-e"u8),
                OsString.FromUnixBytes("alpha"u8),
                OsString.FromUnixBytes("-f"u8),
                OsString.FromUnixBytes("patterns.txt"u8),
                OsString.FromUnixBytes("path.txt"u8),
            ]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--file"u8), OsString.FromUnixBytes("-patterns"u8), OsString.FromUnixBytes("path.txt"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreSequenceEqual<CliPatternSource>([CliPatternSource.File(OsString.FromUnixBytes("patterns.txt"u8))], separate.LowArgs!.PatternSources);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreSequenceEqual<CliPatternSource>(
            [
                CliPatternSource.File(OsString.FromUnixBytes("first.txt"u8)),
                CliPatternSource.File(OsString.FromUnixBytes("second.txt"u8)),
            ],
            inline.LowArgs!.PatternSources);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, ordered.Status);
        Assert.AreSequenceEqual<CliPatternSource>(
            [
                CliPatternSource.Pattern(OsString.FromUnixBytes("alpha"u8)),
                CliPatternSource.File(OsString.FromUnixBytes("patterns.txt"u8)),
            ],
            ordered.LowArgs!.PatternSources);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("alpha"u8)], ordered.LowArgs.Patterns);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], ordered.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, dashValue.Status);
        Assert.AreSequenceEqual<CliPatternSource>([CliPatternSource.File(OsString.FromUnixBytes("-patterns"u8))], dashValue.LowArgs!.PatternSources);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("path.txt"u8)], dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies regexp parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsRegexpParseErrors()
    {
        CliParseResult shortMissing = CliParser.Parse([OsString.FromUnixBytes("-e"u8)]);
        CliParseResult longMissing = CliParser.Parse([OsString.FromUnixBytes("--regexp"u8)]);
        CliParseResult shortInvalidUtf8 = CliParser.Parse(
            [OsString.FromUnixBytes("-e"u8), OsString.FromUnixBytes([(byte)'(', (byte)'?', (byte)'-', (byte)'u', (byte)')', 0xff])]);
        CliParseResult longInvalidUtf8 = CliParser.Parse(
            [OsString.FromUnixBytes("--regexp"u8), OsString.FromUnixBytes([(byte)'(', (byte)'?', (byte)'-', (byte)'u', (byte)')', 0xff])]);

        Assert.AreEqual(CliParseStatus.Error, shortMissing.Status);
        Assert.AreEqual("missing value for flag -e: missing argument for option '-e'", shortMissing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, longMissing.Status);
        Assert.AreEqual("missing value for flag --regexp: missing argument for option '--regexp'", longMissing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, shortInvalidUtf8.Status);
        Assert.AreEqual("error parsing flag -e: value is not valid UTF-8", shortInvalidUtf8.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, longInvalidUtf8.Status);
        Assert.AreEqual("error parsing flag --regexp: value is not valid UTF-8", longInvalidUtf8.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies pattern-file parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsPatternFileParseErrors()
    {
        CliParseResult shortMissing = CliParser.Parse([OsString.FromUnixBytes("-f"u8)]);
        CliParseResult longMissing = CliParser.Parse([OsString.FromUnixBytes("--file"u8)]);

        Assert.AreEqual(CliParseStatus.Error, shortMissing.Status);
        Assert.AreEqual("missing value for flag -f: missing argument for option '-f'", shortMissing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, longMissing.Status);
        Assert.AreEqual("missing value for flag --file: missing argument for option '--file'", longMissing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies <c>--no-config</c> is accepted as a no-op after startup expansion.
    /// </summary>
    [TestMethod]
    public void ParsesNoConfigFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--no-config"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.AreSequenceEqual<OsString>([OsString.FromUnixBytes("needle"u8)], result.LowArgs!.Positional);
    }

    /// <summary>
    /// Verifies <c>--generate</c> selects generated artifact modes with ripgrep's mode override behavior.
    /// </summary>
    [TestMethod]
    public void ParsesGenerateFlags()
    {
        CliParseResult man = CliParser.Parse([OsString.FromUnixBytes("--generate"u8), OsString.FromUnixBytes("man"u8)]);
        CliParseResult bash = CliParser.Parse([OsString.FromUnixBytes("--generate=complete-bash"u8)]);
        CliParseResult zsh = CliParser.Parse([OsString.FromUnixBytes("--generate=complete-zsh"u8)]);
        CliParseResult fish = CliParser.Parse([OsString.FromUnixBytes("--generate=complete-fish"u8)]);
        CliParseResult powershell = CliParser.Parse([OsString.FromUnixBytes("--generate=complete-powershell"u8)]);
        CliParseResult lastGenerateWins = CliParser.Parse(
            [OsString.FromUnixBytes("--generate"u8), OsString.FromUnixBytes("complete-bash"u8), OsString.FromUnixBytes("--generate=man"u8)]);
        CliParseResult searchWins = CliParser.Parse(
            [OsString.FromUnixBytes("--generate"u8), OsString.FromUnixBytes("man"u8), OsString.FromUnixBytes("-l"u8)]);
        CliParseResult jsonResetWins = CliParser.Parse(
            [OsString.FromUnixBytes("--generate"u8), OsString.FromUnixBytes("man"u8), OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("--no-json"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, man.Status);
        Assert.AreEqual(CliGenerateMode.Man, man.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, bash.Status);
        Assert.AreEqual(CliGenerateMode.CompleteBash, bash.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, zsh.Status);
        Assert.AreEqual(CliGenerateMode.CompleteZsh, zsh.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, fish.Status);
        Assert.AreEqual(CliGenerateMode.CompleteFish, fish.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, powershell.Status);
        Assert.AreEqual(CliGenerateMode.CompletePowerShell, powershell.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, lastGenerateWins.Status);
        Assert.AreEqual(CliGenerateMode.Man, lastGenerateWins.LowArgs!.GenerateMode);
        Assert.AreEqual(CliParseStatus.Ok, searchWins.Status);
        Assert.IsNull(searchWins.LowArgs!.GenerateMode);
        Assert.AreEqual(CliSearchMode.FilesWithMatches, searchWins.LowArgs.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, jsonResetWins.Status);
        Assert.IsNull(jsonResetWins.LowArgs!.GenerateMode);
        Assert.AreEqual(CliSearchMode.Standard, jsonResetWins.LowArgs.SearchMode);
    }

    /// <summary>
    /// Verifies <c>--generate</c> parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsGenerateParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--generate"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--generate=foo"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --generate: missing argument for option '--generate'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --generate: choice 'foo' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies remaining non-generate upstream flags are represented in low arguments.
    /// </summary>
    [TestMethod]
    public void ParsesRemainingNonGenerateFlags()
    {
        CliParseResult search = CliParser.Parse(
            [
                OsString.FromUnixBytes("-F"u8),
                OsString.FromUnixBytes("--no-fixed-strings"u8),
                OsString.FromUnixBytes("--dfa-size-limit=9G"u8),
                OsString.FromUnixBytes("--regex-size-limit"u8),
                OsString.FromUnixBytes("2M"u8),
                OsString.FromUnixBytes("--colors"u8),
                OsString.FromUnixBytes("match:fg:magenta"u8),
                OsString.FromUnixBytes("--colors=line:bg:yellow"u8),
                OsString.FromUnixBytes("--hostname-bin"u8),
                OsString.FromUnixBytes("hostname"u8),
                OsString.FromUnixBytes("--hyperlink-format=file://{host}{path}"u8),
                OsString.FromUnixBytes("--stop-on-nonmatch"u8),
                OsString.FromUnixBytes("-U"u8),
                OsString.FromUnixBytes("--no-multiline"u8),
                OsString.FromUnixBytes("--multiline-dotall"u8),
                OsString.FromUnixBytes("--no-multiline-dotall"u8),
                OsString.FromUnixBytes("--no-unicode"u8),
                OsString.FromUnixBytes("--unicode"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult traceWins = CliParser.Parse(
            [OsString.FromUnixBytes("--debug"u8), OsString.FromUnixBytes("--trace"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult debugWins = CliParser.Parse(
            [OsString.FromUnixBytes("--trace"u8), OsString.FromUnixBytes("--debug"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, search.Status);
        Assert.IsFalse(search.LowArgs!.FixedStrings);
        Assert.AreEqual(9UL * 1024UL * 1024UL * 1024UL, search.LowArgs.DfaSizeLimit);
        Assert.AreEqual(2UL * 1024UL * 1024UL, search.LowArgs.RegexSizeLimit);
        Assert.AreSequenceEqual<string>(["match:fg:magenta", "line:bg:yellow"], search.LowArgs.ColorSpecs);
        Assert.AreEqual("hostname", search.LowArgs.HostnameBin);
        Assert.AreEqual("file://{host}{path}", search.LowArgs.HyperlinkFormat);
        Assert.IsFalse(search.LowArgs.StopOnNonmatch);
        Assert.IsFalse(search.LowArgs.Multiline);
        Assert.IsFalse(search.LowArgs.MultilineDotall);
        Assert.IsTrue(search.LowArgs.Unicode);
        Assert.ContainsSingle(search.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, traceWins.Status);
        Assert.AreEqual(CliLoggingMode.Trace, traceWins.LowArgs!.LoggingMode);
        Assert.AreEqual(CliParseStatus.Ok, debugWins.Status);
        Assert.AreEqual(CliLoggingMode.Debug, debugWins.LowArgs!.LoggingMode);
    }

    /// <summary>
    /// Verifies size limit parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsSizeLimitParseErrors()
    {
        byte[] invalidDfaValue = [.. "--dfa-size-limit="u8, 0xFF];
        byte[] invalidRegexValue = [.. "--regex-size-limit="u8, 0xFF];
        CliParseResult dfaInvalidSuffix = CliParser.Parse(
            [OsString.FromUnixBytes("--dfa-size-limit=1k"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult regexInvalidSuffix = CliParser.Parse(
            [OsString.FromUnixBytes("--regex-size-limit=1T"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult dfaInvalidUtf8 = CliParser.Parse(
            [OsString.FromUnixBytes(invalidDfaValue), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult regexInvalidUtf8 = CliParser.Parse(
            [OsString.FromUnixBytes(invalidRegexValue), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, dfaInvalidSuffix.Status);
        Assert.AreEqual(
            "error parsing flag --dfa-size-limit: invalid size: invalid format for size '1k', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            dfaInvalidSuffix.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, regexInvalidSuffix.Status);
        Assert.AreEqual(
            "error parsing flag --regex-size-limit: invalid size: invalid format for size '1T', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            regexInvalidSuffix.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, dfaInvalidUtf8.Status);
        Assert.AreEqual("error parsing flag --dfa-size-limit: value is not valid UTF-8", dfaInvalidUtf8.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, regexInvalidUtf8.Status);
        Assert.AreEqual("error parsing flag --regex-size-limit: value is not valid UTF-8", regexInvalidUtf8.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies invalid <c>--colors</c> values report ripgrep-compatible parser diagnostics.
    /// </summary>
    /// <param name="spec">The invalid color specification.</param>
    /// <param name="expected">The expected diagnostic text.</param>
    [TestMethod]
    [DataRow("bad", "error parsing flag --colors: invalid color spec format: 'bad'. Valid format is '(path|line|column|match|highlight):(fg|bg|style):(value)'.")]
    [DataRow("foo:fg:red", "error parsing flag --colors: unrecognized output type 'foo'. Choose from: path, line, column, match, highlight.")]
    [DataRow("match:what:red", "error parsing flag --colors: unrecognized spec type 'what'. Choose from: fg, bg, style, none.")]
    [DataRow("match:fg:bogus", "error parsing flag --colors: unrecognized color name 'bogus'. Choose from: black, blue, green, red, cyan, magenta, yellow, white")]
    [DataRow("match:style:bad", "error parsing flag --colors: unrecognized style attribute 'bad'. Choose from: nobold, bold, nointense, intense, nounderline, underline, noitalic, italic.")]
    [DataRow("match:fg", "error parsing flag --colors: invalid color spec format: 'match:fg'. Valid format is '(path|line|column|match|highlight):(fg|bg|style):(value)'.")]
    [DataRow("match:fg:999", "error parsing flag --colors: unrecognized ansi256 color number, should be '[0-255]' (or a hex number), but is '999'")]
    [DataRow("match:fg:1,2", "error parsing flag --colors: unrecognized RGB color triple, should be '[0-255],[0-255],[0-255]' (or a hex triple), but is '1,2'")]
    public void ReportsColorSpecParseErrors(string spec, string expected)
    {
        CliParseResult result = CliParser.Parse([OsString.FromText("--colors"), OsString.FromText(spec), OsString.FromText("needle")]);

        Assert.AreEqual(CliParseStatus.Error, result.Status);
        Assert.AreEqual(expected, result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies <c>--hyperlink-format</c> aliases normalize like ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesHyperlinkFormatAliases()
    {
        CliParseResult none = CliParser.Parse([OsString.FromText("--hyperlink-format"), OsString.FromText("none"), OsString.FromText("needle")]);
        CliParseResult defaultAlias = CliParser.Parse([OsString.FromText("--hyperlink-format"), OsString.FromText("default"), OsString.FromText("needle")]);
        CliParseResult file = CliParser.Parse([OsString.FromText("--hyperlink-format"), OsString.FromText("file"), OsString.FromText("needle")]);
        CliParseResult lastWins = CliParser.Parse(
            [
                OsString.FromText("--hyperlink-format"),
                OsString.FromText("file"),
                OsString.FromText("--hyperlink-format=grep+"),
                OsString.FromText("needle"),
            ]);
        string expectedDefault = OperatingSystem.IsWindows() ? "file://{path}" : "file://{host}{path}";

        Assert.AreEqual(CliParseStatus.Ok, none.Status);
        Assert.AreEqual(string.Empty, none.LowArgs!.HyperlinkFormat);
        Assert.AreEqual(CliParseStatus.Ok, defaultAlias.Status);
        Assert.AreEqual(expectedDefault, defaultAlias.LowArgs!.HyperlinkFormat);
        Assert.AreEqual(CliParseStatus.Ok, file.Status);
        Assert.AreEqual("file://{host}{path}", file.LowArgs!.HyperlinkFormat);
        Assert.AreEqual(CliParseStatus.Ok, lastWins.Status);
        Assert.AreEqual("grep+://{path}:{line}", lastWins.LowArgs!.HyperlinkFormat);
    }

    /// <summary>
    /// Verifies invalid <c>--hyperlink-format</c> values report ripgrep-compatible parser diagnostics.
    /// </summary>
    /// <param name="format">The invalid hyperlink format.</param>
    /// <param name="expected">The expected diagnostic text.</param>
    [TestMethod]
    [DataRow("foo://bar", "error parsing flag --hyperlink-format: invalid hyperlink format: at least a {path} variable is required in a hyperlink format, or otherwise use a valid alias: default, none, cursor, file, grep+, kitty, macvim, textmate, vscode, vscode-insiders, vscodium")]
    [DataRow("foo://{line}", "error parsing flag --hyperlink-format: invalid hyperlink format: the {path} variable is required in a hyperlink format")]
    [DataRow("foo://{path", "error parsing flag --hyperlink-format: invalid hyperlink format: unclosed variable: found '{' without a corresponding '}' following it")]
    [DataRow("foo://{path}:{column}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format contains a {column} variable, but no {line} variable is present")]
    [DataRow("{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [DataRow(":{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [DataRow("f*:{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [DataRow("foo://{bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: invalid hyperlink format variable: 'bar', choose from: path, line, column, host, wslprefix")]
    [DataRow("foo://{}}bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: invalid hyperlink format variable: '', choose from: path, line, column, host, wslprefix")]
    [DataRow("foo://{{bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: unopened variable: found '}' without a corresponding '{' preceding it")]
    public void ReportsHyperlinkFormatParseErrors(string format, string expected)
    {
        CliParseResult result = CliParser.Parse([OsString.FromText("--hyperlink-format"), OsString.FromText(format), OsString.FromText("needle")]);

        Assert.AreEqual(CliParseStatus.Error, result.Status);
        Assert.AreEqual(expected, result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies binary text-mode flags are parsed with ripgrep's order-sensitive behavior.
    /// </summary>
    [TestMethod]
    public void ParsesBinaryTextModeFlags()
    {
        CliParseResult enabledShort = CliParser.Parse(
            [OsString.FromUnixBytes("-a"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabledLong = CliParser.Parse(
            [OsString.FromUnixBytes("--text"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult binaryWins = CliParser.Parse(
            [OsString.FromUnixBytes("-a"u8), OsString.FromUnixBytes("--binary"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult textWins = CliParser.Parse(
            [OsString.FromUnixBytes("--binary"u8), OsString.FromUnixBytes("-a"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult noText = CliParser.Parse(
            [OsString.FromUnixBytes("-a"u8), OsString.FromUnixBytes("--no-text"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult noBinary = CliParser.Parse(
            [OsString.FromUnixBytes("-a"u8), OsString.FromUnixBytes("--no-binary"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabledShort.Status);
        Assert.IsTrue(enabledShort.LowArgs!.TextMode);
        Assert.AreEqual(CliParseStatus.Ok, enabledLong.Status);
        Assert.IsTrue(enabledLong.LowArgs!.TextMode);
        Assert.AreEqual(CliParseStatus.Ok, binaryWins.Status);
        Assert.IsFalse(binaryWins.LowArgs!.TextMode);
        Assert.AreEqual(CliParseStatus.Ok, textWins.Status);
        Assert.IsTrue(textWins.LowArgs!.TextMode);
        Assert.AreEqual(CliParseStatus.Ok, noText.Status);
        Assert.IsFalse(noText.LowArgs!.TextMode);
        Assert.AreEqual(CliParseStatus.Ok, noBinary.Status);
        Assert.IsFalse(noBinary.LowArgs!.TextMode);
    }

    /// <summary>
    /// Verifies preprocessing and compressed-search flags use ripgrep's override behavior.
    /// </summary>
    [TestMethod]
    public void ParsesPreprocessorAndSearchZipFlags()
    {
        CliParseResult zip = CliParser.Parse(
            [OsString.FromUnixBytes("-z"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult noZip = CliParser.Parse(
            [OsString.FromUnixBytes("--search-zip"u8), OsString.FromUnixBytes("--no-search-zip"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult pre = CliParser.Parse(
            [OsString.FromUnixBytes("--pre"u8), OsString.FromUnixBytes("cat"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult emptyPre = CliParser.Parse(
            [OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("--pre="u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult noPre = CliParser.Parse(
            [OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("--no-pre"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult preAfterNoPre = CliParser.Parse(
            [OsString.FromUnixBytes("--no-pre"u8), OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult zipOverridesPre = CliParser.Parse(
            [OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("-z"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult preOverridesZip = CliParser.Parse(
            [OsString.FromUnixBytes("-z"u8), OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult preGlob = CliParser.Parse(
            [OsString.FromUnixBytes("--pre-glob"u8), OsString.FromUnixBytes("*.xz"u8), OsString.FromUnixBytes("--pre-glob=*.gz"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, zip.Status);
        Assert.IsTrue(zip.LowArgs!.SearchZip);
        Assert.IsNull(zip.LowArgs.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, noZip.Status);
        Assert.IsFalse(noZip.LowArgs!.SearchZip);
        Assert.AreEqual(CliParseStatus.Ok, pre.Status);
        Assert.AreEqual("cat", pre.LowArgs!.Preprocessor);
        Assert.IsFalse(pre.LowArgs.SearchZip);
        Assert.AreEqual(CliParseStatus.Ok, emptyPre.Status);
        Assert.IsNull(emptyPre.LowArgs!.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, noPre.Status);
        Assert.IsNull(noPre.LowArgs!.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, preAfterNoPre.Status);
        Assert.AreEqual("cat", preAfterNoPre.LowArgs!.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, zipOverridesPre.Status);
        Assert.IsTrue(zipOverridesPre.LowArgs!.SearchZip);
        Assert.IsNull(zipOverridesPre.LowArgs.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, preOverridesZip.Status);
        Assert.IsFalse(preOverridesZip.LowArgs!.SearchZip);
        Assert.AreEqual("cat", preOverridesZip.LowArgs.Preprocessor);
        Assert.AreEqual(CliParseStatus.Ok, preGlob.Status);
        Assert.AreSequenceEqual<string>(["*.xz", "*.gz"], preGlob.LowArgs!.PreprocessorGlobs);
    }

    /// <summary>
    /// Verifies thread-count flags accept ripgrep's separate and inline value forms.
    /// </summary>
    [TestMethod]
    public void ParsesThreadsFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--threads"u8), OsString.FromUnixBytes("2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult zero = CliParser.Parse(
            [OsString.FromUnixBytes("--threads=0"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--threads=0"u8), OsString.FromUnixBytes("-j2"u8), OsString.FromUnixBytes("-j=4"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreEqual(2UL, separate.LowArgs!.Threads);
        Assert.ContainsSingle(separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, zero.Status);
        Assert.IsNull(zero.LowArgs!.Threads);
        Assert.ContainsSingle(zero.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreEqual(4UL, inline.LowArgs!.Threads);
        Assert.ContainsSingle(inline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies thread-count diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsThreadsParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--threads"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--threads=abc"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInvalid = CliParser.Parse([OsString.FromUnixBytes("-j"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --threads: missing argument for option '--threads'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --threads: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, shortInvalid.Status);
        Assert.AreEqual("error parsing flag -j: value is not a valid number: invalid digit found in string", shortInvalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies buffering and memory-map switches use ripgrep's mode-setting behavior.
    /// </summary>
    [TestMethod]
    public void ParsesBufferingAndMmapFlags()
    {
        CliParseResult defaultModes = CliParser.Parse([OsString.FromUnixBytes("needle"u8)]);
        CliParseResult buffered = CliParser.Parse(
            [
                OsString.FromUnixBytes("--line-buffered"u8),
                OsString.FromUnixBytes("--block-buffered"u8),
                OsString.FromUnixBytes("--no-block-buffered"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult mmap = CliParser.Parse(
            [
                OsString.FromUnixBytes("--mmap"u8),
                OsString.FromUnixBytes("--no-mmap"u8),
                OsString.FromUnixBytes("--mmap"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Ok, defaultModes.Status);
        Assert.AreEqual(CliBufferMode.Auto, defaultModes.LowArgs!.BufferMode);
        Assert.AreEqual(CliMmapMode.Auto, defaultModes.LowArgs.MmapMode);
        Assert.AreEqual(CliParseStatus.Ok, buffered.Status);
        Assert.AreEqual(CliBufferMode.Auto, buffered.LowArgs!.BufferMode);
        Assert.ContainsSingle(buffered.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, mmap.Status);
        Assert.AreEqual(CliMmapMode.AlwaysTryMmap, mmap.LowArgs!.MmapMode);
        Assert.ContainsSingle(mmap.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies message switches use ripgrep's last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesMessageFlags()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--messages"u8), OsString.FromUnixBytes("--no-messages"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-messages"u8), OsString.FromUnixBytes("--messages"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Messages);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Messages);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch is parsed as a search switch.
    /// </summary>
    [TestMethod]
    public void ParsesStopOnNonmatchFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--stop-on-nonmatch"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.IsTrue(result.LowArgs!.StopOnNonmatch);
        Assert.ContainsSingle(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies CRLF mode flags use ripgrep's last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesCrlfFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--crlf"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--crlf"u8), OsString.FromUnixBytes("--no-crlf"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult nullData = CliParser.Parse(
            [OsString.FromUnixBytes("--null-data"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult crlfThenNullData = CliParser.Parse(
            [OsString.FromUnixBytes("--crlf"u8), OsString.FromUnixBytes("--null-data"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult nullDataThenCrlf = CliParser.Parse(
            [OsString.FromUnixBytes("--null-data"u8), OsString.FromUnixBytes("--crlf"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult nullDataThenNoCrlf = CliParser.Parse(
            [OsString.FromUnixBytes("--null-data"u8), OsString.FromUnixBytes("--no-crlf"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Crlf);
        Assert.IsFalse(enabled.LowArgs.NullData);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Crlf);
        Assert.IsFalse(disabled.LowArgs.NullData);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, nullData.Status);
        Assert.IsTrue(nullData.LowArgs!.NullData);
        Assert.IsFalse(nullData.LowArgs.Crlf);
        Assert.ContainsSingle(nullData.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, crlfThenNullData.Status);
        Assert.IsTrue(crlfThenNullData.LowArgs!.NullData);
        Assert.IsFalse(crlfThenNullData.LowArgs.Crlf);
        Assert.ContainsSingle(crlfThenNullData.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, nullDataThenCrlf.Status);
        Assert.IsTrue(nullDataThenCrlf.LowArgs!.Crlf);
        Assert.IsFalse(nullDataThenCrlf.LowArgs.NullData);
        Assert.ContainsSingle(nullDataThenCrlf.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, nullDataThenNoCrlf.Status);
        Assert.IsTrue(nullDataThenNoCrlf.LowArgs!.NullData);
        Assert.IsFalse(nullDataThenNoCrlf.LowArgs.Crlf);
        Assert.ContainsSingle(nullDataThenNoCrlf.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies regex engine switches use ripgrep's last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesRegexEngineFlags()
    {
        CliParseResult pcre2 = CliParser.Parse(
            [OsString.FromUnixBytes("--engine=auto"u8), OsString.FromUnixBytes("-P"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult defaultEngine = CliParser.Parse(
            [OsString.FromUnixBytes("--engine"u8), OsString.FromUnixBytes("pcre2"u8), OsString.FromUnixBytes("--no-pcre2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult hybrid = CliParser.Parse(
            [OsString.FromUnixBytes("--auto-hybrid-regex"u8), OsString.FromUnixBytes("--no-auto-hybrid-regex"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult pcre2Unicode = CliParser.Parse(
            [OsString.FromUnixBytes("--no-pcre2-unicode"u8), OsString.FromUnixBytes("--pcre2-unicode"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult pcre2ThenHybrid = CliParser.Parse(
            [OsString.FromUnixBytes("-P"u8), OsString.FromUnixBytes("--auto-hybrid-regex"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult hybridThenPcre2 = CliParser.Parse(
            [OsString.FromUnixBytes("--auto-hybrid-regex"u8), OsString.FromUnixBytes("-P"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult pcre2ThenNoHybrid = CliParser.Parse(
            [OsString.FromUnixBytes("--engine=pcre2"u8), OsString.FromUnixBytes("--no-auto-hybrid-regex"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, pcre2.Status);
        Assert.AreEqual(CliRegexEngine.Pcre2, pcre2.LowArgs!.RegexEngine);
        Assert.ContainsSingle(pcre2.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, defaultEngine.Status);
        Assert.AreEqual(CliRegexEngine.Default, defaultEngine.LowArgs!.RegexEngine);
        Assert.ContainsSingle(defaultEngine.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, hybrid.Status);
        Assert.IsFalse(hybrid.LowArgs!.AutoHybridRegex);
        Assert.AreEqual(CliRegexEngine.Default, hybrid.LowArgs.RegexEngine);
        Assert.ContainsSingle(hybrid.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, pcre2Unicode.Status);
        Assert.IsTrue(pcre2Unicode.LowArgs!.Pcre2Unicode);
        Assert.ContainsSingle(pcre2Unicode.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, pcre2ThenHybrid.Status);
        Assert.AreEqual(CliRegexEngine.Auto, pcre2ThenHybrid.LowArgs!.RegexEngine);
        Assert.AreEqual(CliParseStatus.Ok, hybridThenPcre2.Status);
        Assert.AreEqual(CliRegexEngine.Pcre2, hybridThenPcre2.LowArgs!.RegexEngine);
        Assert.AreEqual(CliParseStatus.Ok, pcre2ThenNoHybrid.Status);
        Assert.AreEqual(CliRegexEngine.Default, pcre2ThenNoHybrid.LowArgs!.RegexEngine);
    }

    /// <summary>
    /// Verifies regex engine parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsRegexEngineParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--engine"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--engine=bogus"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --engine: missing argument for option '--engine'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --engine: unrecognized regex engine 'bogus'", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies encoding flags use ripgrep's last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesEncodingFlags()
    {
        CliParseResult none = CliParser.Parse(
            [OsString.FromUnixBytes("--encoding"u8), OsString.FromUnixBytes("none"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inlineNone = CliParser.Parse(
            [OsString.FromUnixBytes("--encoding=none"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortNone = CliParser.Parse(
            [OsString.FromUnixBytes("-Enone"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult noEncoding = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("none"u8), OsString.FromUnixBytes("--no-encoding"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult lastEncoding = CliParser.Parse(
            [OsString.FromUnixBytes("--no-encoding"u8), OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("utf-16"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1252 = CliParser.Parse(
            [OsString.FromUnixBytes("--encoding=LATIN1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88591 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes(" iso-8859-1 "u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult utf8Alias = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("unicode20utf8"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult utf16LeAlias = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("ucs-2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult utf16BeAlias = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("unicodefffe"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult eucKr = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-949"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult eucJp = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("x-euc-jp"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult big5 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("big5-hkscs"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult gb18030 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("gb18030"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult gbk = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("gb2312"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shiftJis = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-31j"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult ibm866 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("ibm866"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88592 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("latin2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88593 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("latin3"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88594 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("latin4"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88595 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("cyrillic"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88596 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("arabic"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88597 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("greek"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88598 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("hebrew"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso88598I = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("logical"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso885910 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("latin6"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso885913 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("iso-8859-13"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso885914 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("iso-8859-14"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso885915 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("l9"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso885916 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("iso-8859-16"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult iso2022Jp = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("csiso2022jp"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult koi8r = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("koi8-r"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult koi8u = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("koi8-u"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult macintosh = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("macintosh"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows874 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-874"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1250 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1250"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1251 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1251"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1253 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1253"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1254 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1254"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1255 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1255"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1256 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1256"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1257 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1257"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult windows1258 = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("windows-1258"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult xMacCyrillic = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("x-mac-cyrillic"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult xUserDefined = CliParser.Parse(
            [OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("x-user-defined"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, none.Status);
        Assert.AreEqual(CliEncodingMode.None, none.LowArgs!.EncodingMode);
        Assert.ContainsSingle(none.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inlineNone.Status);
        Assert.AreEqual(CliEncodingMode.None, inlineNone.LowArgs!.EncodingMode);
        Assert.ContainsSingle(inlineNone.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortNone.Status);
        Assert.AreEqual(CliEncodingMode.None, shortNone.LowArgs!.EncodingMode);
        Assert.ContainsSingle(shortNone.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, noEncoding.Status);
        Assert.AreEqual(CliEncodingMode.Auto, noEncoding.LowArgs!.EncodingMode);
        Assert.ContainsSingle(noEncoding.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, lastEncoding.Status);
        Assert.AreEqual(CliEncodingMode.Utf16, lastEncoding.LowArgs!.EncodingMode);
        Assert.ContainsSingle(lastEncoding.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1252.Status);
        Assert.AreEqual(CliEncodingMode.Windows1252, windows1252.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1252.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88591.Status);
        Assert.AreEqual(CliEncodingMode.Windows1252, iso88591.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88591.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, utf8Alias.Status);
        Assert.AreEqual(CliEncodingMode.Utf8, utf8Alias.LowArgs!.EncodingMode);
        Assert.ContainsSingle(utf8Alias.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, utf16LeAlias.Status);
        Assert.AreEqual(CliEncodingMode.Utf16Le, utf16LeAlias.LowArgs!.EncodingMode);
        Assert.ContainsSingle(utf16LeAlias.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, utf16BeAlias.Status);
        Assert.AreEqual(CliEncodingMode.Utf16Be, utf16BeAlias.LowArgs!.EncodingMode);
        Assert.ContainsSingle(utf16BeAlias.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, eucKr.Status);
        Assert.AreEqual(CliEncodingMode.EucKr, eucKr.LowArgs!.EncodingMode);
        Assert.ContainsSingle(eucKr.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, eucJp.Status);
        Assert.AreEqual(CliEncodingMode.EucJp, eucJp.LowArgs!.EncodingMode);
        Assert.ContainsSingle(eucJp.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, big5.Status);
        Assert.AreEqual(CliEncodingMode.Big5, big5.LowArgs!.EncodingMode);
        Assert.ContainsSingle(big5.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, gb18030.Status);
        Assert.AreEqual(CliEncodingMode.Gb18030, gb18030.LowArgs!.EncodingMode);
        Assert.ContainsSingle(gb18030.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, gbk.Status);
        Assert.AreEqual(CliEncodingMode.Gbk, gbk.LowArgs!.EncodingMode);
        Assert.ContainsSingle(gbk.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shiftJis.Status);
        Assert.AreEqual(CliEncodingMode.ShiftJis, shiftJis.LowArgs!.EncodingMode);
        Assert.ContainsSingle(shiftJis.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, ibm866.Status);
        Assert.AreEqual(CliEncodingMode.Ibm866, ibm866.LowArgs!.EncodingMode);
        Assert.ContainsSingle(ibm866.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88592.Status);
        Assert.AreEqual(CliEncodingMode.Iso88592, iso88592.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88592.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88593.Status);
        Assert.AreEqual(CliEncodingMode.Iso88593, iso88593.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88593.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88594.Status);
        Assert.AreEqual(CliEncodingMode.Iso88594, iso88594.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88594.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88595.Status);
        Assert.AreEqual(CliEncodingMode.Iso88595, iso88595.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88595.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88596.Status);
        Assert.AreEqual(CliEncodingMode.Iso88596, iso88596.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88596.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88597.Status);
        Assert.AreEqual(CliEncodingMode.Iso88597, iso88597.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88597.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88598.Status);
        Assert.AreEqual(CliEncodingMode.Iso88598, iso88598.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88598.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso88598I.Status);
        Assert.AreEqual(CliEncodingMode.Iso88598I, iso88598I.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso88598I.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso885910.Status);
        Assert.AreEqual(CliEncodingMode.Iso885910, iso885910.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso885910.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso885913.Status);
        Assert.AreEqual(CliEncodingMode.Iso885913, iso885913.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso885913.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso885914.Status);
        Assert.AreEqual(CliEncodingMode.Iso885914, iso885914.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso885914.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso885915.Status);
        Assert.AreEqual(CliEncodingMode.Iso885915, iso885915.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso885915.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso885916.Status);
        Assert.AreEqual(CliEncodingMode.Iso885916, iso885916.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso885916.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, iso2022Jp.Status);
        Assert.AreEqual(CliEncodingMode.Iso2022Jp, iso2022Jp.LowArgs!.EncodingMode);
        Assert.ContainsSingle(iso2022Jp.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, koi8r.Status);
        Assert.AreEqual(CliEncodingMode.Koi8R, koi8r.LowArgs!.EncodingMode);
        Assert.ContainsSingle(koi8r.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, koi8u.Status);
        Assert.AreEqual(CliEncodingMode.Koi8U, koi8u.LowArgs!.EncodingMode);
        Assert.ContainsSingle(koi8u.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, macintosh.Status);
        Assert.AreEqual(CliEncodingMode.Macintosh, macintosh.LowArgs!.EncodingMode);
        Assert.ContainsSingle(macintosh.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows874.Status);
        Assert.AreEqual(CliEncodingMode.Windows874, windows874.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows874.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1250.Status);
        Assert.AreEqual(CliEncodingMode.Windows1250, windows1250.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1250.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1251.Status);
        Assert.AreEqual(CliEncodingMode.Windows1251, windows1251.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1251.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1253.Status);
        Assert.AreEqual(CliEncodingMode.Windows1253, windows1253.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1253.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1254.Status);
        Assert.AreEqual(CliEncodingMode.Windows1254, windows1254.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1254.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1255.Status);
        Assert.AreEqual(CliEncodingMode.Windows1255, windows1255.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1255.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1256.Status);
        Assert.AreEqual(CliEncodingMode.Windows1256, windows1256.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1256.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1257.Status);
        Assert.AreEqual(CliEncodingMode.Windows1257, windows1257.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1257.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, windows1258.Status);
        Assert.AreEqual(CliEncodingMode.Windows1258, windows1258.LowArgs!.EncodingMode);
        Assert.ContainsSingle(windows1258.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, xMacCyrillic.Status);
        Assert.AreEqual(CliEncodingMode.XMacCyrillic, xMacCyrillic.LowArgs!.EncodingMode);
        Assert.ContainsSingle(xMacCyrillic.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, xUserDefined.Status);
        Assert.AreEqual(CliEncodingMode.XUserDefined, xUserDefined.LowArgs!.EncodingMode);
        Assert.ContainsSingle(xUserDefined.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies encoding parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsEncodingParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--encoding"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("foo"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult upperAuto = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("AUTO"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult trimmedNone = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes(" none "u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --encoding: missing argument for option '--encoding'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag -E: grep config error: unknown encoding: foo", invalid.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, upperAuto.Status);
        Assert.AreEqual("error parsing flag -E: grep config error: unknown encoding: AUTO", upperAuto.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, trimmedNone.Status);
        Assert.AreEqual("error parsing flag -E: grep config error: unknown encoding:  none ", trimmedNone.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies line-number flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesLineNumberFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("-N"u8), OsString.FromUnixBytes("--line-number"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-n"u8), OsString.FromUnixBytes("--no-line-number"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.LineNumber);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.LineNumber);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies byte-offset flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesByteOffsetFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-byte-offset"u8), OsString.FromUnixBytes("-b"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--byte-offset"u8), OsString.FromUnixBytes("--no-byte-offset"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.ByteOffset);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.ByteOffset);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies column flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesColumnFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-column"u8), OsString.FromUnixBytes("--column"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--column"u8), OsString.FromUnixBytes("--no-column"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Column);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Column);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies count mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesCountModeFlags()
    {
        CliParseResult count = CliParser.Parse(
            [OsString.FromUnixBytes("--count-matches"u8), OsString.FromUnixBytes("-c"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult countMatches = CliParser.Parse(
            [OsString.FromUnixBytes("--count"u8), OsString.FromUnixBytes("--count-matches"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, count.Status);
        Assert.AreEqual(CliSearchMode.Count, count.LowArgs!.SearchMode);
        Assert.ContainsSingle(count.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, countMatches.Status);
        Assert.AreEqual(CliSearchMode.CountMatches, countMatches.LowArgs!.SearchMode);
        Assert.ContainsSingle(countMatches.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies include-zero flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesIncludeZeroFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-include-zero"u8), OsString.FromUnixBytes("--include-zero"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--include-zero"u8), OsString.FromUnixBytes("--no-include-zero"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.IncludeZero);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.IncludeZero);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-count flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesMaxCountFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-m"u8), OsString.FromUnixBytes("5"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-m5"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longInline = CliParser.Parse(
            [OsString.FromUnixBytes("-m"u8), OsString.FromUnixBytes("5"u8), OsString.FromUnixBytes("--max-count=10"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortSeparate.Status);
        Assert.AreEqual(5UL, shortSeparate.LowArgs!.MaxCount);
        Assert.ContainsSingle(shortSeparate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortInline.Status);
        Assert.AreEqual(5UL, shortInline.LowArgs!.MaxCount);
        Assert.ContainsSingle(shortInline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longInline.Status);
        Assert.AreEqual(10UL, longInline.LowArgs!.MaxCount);
        Assert.ContainsSingle(longInline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-count parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsMaxCountParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("-m"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--max-count=x"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag -m: missing argument for option '-m'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --max-count: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-columns flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesMaxColumnsFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-M"u8), OsString.FromUnixBytes("12"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-M12"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longInline = CliParser.Parse(
            [OsString.FromUnixBytes("-M"u8), OsString.FromUnixBytes("12"u8), OsString.FromUnixBytes("--max-columns=16"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortSeparate.Status);
        Assert.AreEqual(12UL, shortSeparate.LowArgs!.MaxColumns);
        Assert.ContainsSingle(shortSeparate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortInline.Status);
        Assert.AreEqual(12UL, shortInline.LowArgs!.MaxColumns);
        Assert.ContainsSingle(shortInline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longInline.Status);
        Assert.AreEqual(16UL, longInline.LowArgs!.MaxColumns);
        Assert.ContainsSingle(longInline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-columns preview toggles use last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesMaxColumnsPreviewFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--max-columns-preview"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--max-columns-preview"u8), OsString.FromUnixBytes("--no-max-columns-preview"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.MaxColumnsPreview);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.MaxColumnsPreview);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-columns parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsMaxColumnsParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-columns"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-Mx"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --max-columns: missing argument for option '--max-columns'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag -M: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies replacement flags accept separate, inline and empty byte values.
    /// </summary>
    [TestMethod]
    public void ParsesReplacementFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-r"u8), OsString.FromUnixBytes("X"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-rY"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult empty = CliParser.Parse(
            [OsString.FromUnixBytes("--replace="u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortSeparate.Status);
        Assert.AreSequenceEqual("X"u8.ToArray(), shortSeparate.LowArgs!.Replacement!.Value.ToArray());
        Assert.ContainsSingle(shortSeparate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortInline.Status);
        Assert.AreSequenceEqual("Y"u8.ToArray(), shortInline.LowArgs!.Replacement!.Value.ToArray());
        Assert.ContainsSingle(shortInline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, empty.Status);
        Assert.IsEmpty(empty.LowArgs!.Replacement!.Value.ToArray());
        Assert.ContainsSingle(empty.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies replacement parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsReplacementParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("-r"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag -r: missing argument for option '-r'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies color flags accept ripgrep's supported choices.
    /// </summary>
    [TestMethod]
    public void ParsesColorFlags()
    {
        CliParseResult always = CliParser.Parse(
            [OsString.FromUnixBytes("--color=always"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--color"u8), OsString.FromUnixBytes("never"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult ansi = CliParser.Parse(
            [OsString.FromUnixBytes("--color=ansi"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, always.Status);
        Assert.AreEqual(CliColorMode.Always, always.LowArgs!.ColorMode);
        Assert.ContainsSingle(always.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreEqual(CliColorMode.Never, separate.LowArgs!.ColorMode);
        Assert.ContainsSingle(separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, ansi.Status);
        Assert.AreEqual(CliColorMode.Ansi, ansi.LowArgs!.ColorMode);
        Assert.ContainsSingle(ansi.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies pretty output is parsed as ripgrep's color, heading and line-number alias.
    /// </summary>
    [TestMethod]
    public void ParsesPrettyFlag()
    {
        CliParseResult pretty = CliParser.Parse(
            [OsString.FromUnixBytes("-p"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult overridden = CliParser.Parse(
            [OsString.FromUnixBytes("--pretty"u8), OsString.FromUnixBytes("--color=never"u8), OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("-N"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, pretty.Status);
        Assert.AreEqual(CliColorMode.Always, pretty.LowArgs!.ColorMode);
        Assert.IsTrue(pretty.LowArgs.Heading);
        Assert.IsTrue(pretty.LowArgs.LineNumber);
        Assert.IsTrue(pretty.LowArgs.LineNumberSpecified);
        Assert.ContainsSingle(pretty.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, overridden.Status);
        Assert.AreEqual(CliColorMode.Never, overridden.LowArgs!.ColorMode);
        Assert.IsFalse(overridden.LowArgs.Heading);
        Assert.IsFalse(overridden.LowArgs.LineNumber);
        Assert.IsTrue(overridden.LowArgs.LineNumberSpecified);
        Assert.ContainsSingle(overridden.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrestricted flags apply ripgrep's repeated filtering levels.
    /// </summary>
    [TestMethod]
    public void ParsesUnrestrictedFlags()
    {
        CliParseResult one = CliParser.Parse(
            [OsString.FromUnixBytes("-u"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult two = CliParser.Parse(
            [OsString.FromUnixBytes("--unrestricted"u8), OsString.FromUnixBytes("-u"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult three = CliParser.Parse(
            [OsString.FromUnixBytes("-uuu"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, one.Status);
        Assert.AreEqual(1, one.LowArgs!.UnrestrictedCount);
        Assert.IsFalse(one.LowArgs.RespectIgnoreFiles);
        Assert.IsTrue(one.LowArgs.RespectExplicitIgnoreFiles);
        Assert.IsFalse(one.LowArgs.IncludeHidden);
        Assert.IsFalse(one.LowArgs.SearchBinaryFiles);
        Assert.ContainsSingle(one.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, two.Status);
        Assert.AreEqual(2, two.LowArgs!.UnrestrictedCount);
        Assert.IsFalse(two.LowArgs.RespectIgnoreFiles);
        Assert.IsTrue(two.LowArgs.IncludeHidden);
        Assert.IsFalse(two.LowArgs.SearchBinaryFiles);
        Assert.ContainsSingle(two.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, three.Status);
        Assert.AreEqual(3, three.LowArgs!.UnrestrictedCount);
        Assert.IsFalse(three.LowArgs.RespectIgnoreFiles);
        Assert.IsTrue(three.LowArgs.IncludeHidden);
        Assert.IsTrue(three.LowArgs.SearchBinaryFiles);
        Assert.IsFalse(three.LowArgs.TextMode);
        Assert.ContainsSingle(three.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrestricted repeat-limit diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsUnrestrictedRepeatErrors()
    {
        CliParseResult shortError = CliParser.Parse([OsString.FromUnixBytes("-uuuu"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longError = CliParser.Parse(
            [
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Error, shortError.Status);
        Assert.AreEqual("error parsing flag -u: flag can only be repeated up to 3 times", shortError.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, longError.Status);
        Assert.AreEqual("error parsing flag --unrestricted: flag can only be repeated up to 3 times", longError.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies color parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsColorParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--color"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--color=true"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --color: missing argument for option '--color'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --color: choice 'true' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies context flags accept separate and inline values.
    /// </summary>
    [TestMethod]
    public void ParsesContextFlags()
    {
        CliParseResult after = CliParser.Parse(
            [OsString.FromUnixBytes("-A=2"u8), OsString.FromUnixBytes("--after-context=3"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult before = CliParser.Parse(
            [OsString.FromUnixBytes("-B2"u8), OsString.FromUnixBytes("--before-context"u8), OsString.FromUnixBytes("3"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult context = CliParser.Parse(
            [OsString.FromUnixBytes("-C"u8), OsString.FromUnixBytes("2"u8), OsString.FromUnixBytes("--context=3"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, after.Status);
        Assert.AreEqual(3UL, after.LowArgs!.AfterContext);
        Assert.AreEqual(0UL, after.LowArgs.BeforeContext);
        Assert.ContainsSingle(after.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, before.Status);
        Assert.AreEqual(3UL, before.LowArgs!.BeforeContext);
        Assert.AreEqual(0UL, before.LowArgs.AfterContext);
        Assert.ContainsSingle(before.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, context.Status);
        Assert.AreEqual(3UL, context.LowArgs!.BeforeContext);
        Assert.AreEqual(3UL, context.LowArgs.AfterContext);
        Assert.ContainsSingle(context.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies before and after context flags override context defaults regardless of order.
    /// </summary>
    [TestMethod]
    public void ParsesContextFlagPrecedence()
    {
        CliParseResult afterWins = CliParser.Parse(
            [OsString.FromUnixBytes("-A2"u8), OsString.FromUnixBytes("-C1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult beforeWins = CliParser.Parse(
            [OsString.FromUnixBytes("-C1"u8), OsString.FromUnixBytes("-B2"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, afterWins.Status);
        Assert.AreEqual(1UL, afterWins.LowArgs!.BeforeContext);
        Assert.AreEqual(2UL, afterWins.LowArgs.AfterContext);
        Assert.ContainsSingle(afterWins.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, beforeWins.Status);
        Assert.AreEqual(2UL, beforeWins.LowArgs!.BeforeContext);
        Assert.AreEqual(1UL, beforeWins.LowArgs.AfterContext);
        Assert.ContainsSingle(beforeWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies passthrough flags use ripgrep's order-sensitive context precedence.
    /// </summary>
    [TestMethod]
    public void ParsesPassthruPrecedence()
    {
        CliParseResult passthruWins = CliParser.Parse(
            [OsString.FromUnixBytes("-A1"u8), OsString.FromUnixBytes("--passthrough"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult contextWins = CliParser.Parse(
            [OsString.FromUnixBytes("--passthru"u8), OsString.FromUnixBytes("-A1"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, passthruWins.Status);
        Assert.IsTrue(passthruWins.LowArgs!.Passthru);
        Assert.AreEqual(1UL, passthruWins.LowArgs.AfterContext);
        Assert.ContainsSingle(passthruWins.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, contextWins.Status);
        Assert.IsFalse(contextWins.LowArgs!.Passthru);
        Assert.AreEqual(1UL, contextWins.LowArgs.AfterContext);
        Assert.ContainsSingle(contextWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies context parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsContextParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--context"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-Ax"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --context: missing argument for option '--context'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag -A: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies printer separator flags accept separate, inline, empty, and escaped byte values.
    /// </summary>
    [TestMethod]
    public void ParsesOutputSeparatorFlags()
    {
        CliParseResult result = CliParser.Parse(
            [
                OsString.FromUnixBytes("--field-match-separator"u8),
                OsString.FromUnixBytes("|"u8),
                OsString.FromUnixBytes("--field-context-separator=\\t"u8),
                OsString.FromUnixBytes("--context-separator=\\x7f"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult empty = CliParser.Parse(
            [OsString.FromUnixBytes("--context-separator="u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult literalInvalidEscape = CliParser.Parse(
            [OsString.FromUnixBytes("--field-match-separator=\\x0"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.AreSequenceEqual<byte>([(byte)'|'], result.LowArgs!.FieldMatchSeparator.ToArray());
        Assert.AreSequenceEqual<byte>([(byte)'\t'], result.LowArgs.FieldContextSeparator.ToArray());
        Assert.AreSequenceEqual<byte>([(byte)0x7f], result.LowArgs.ContextSeparator.ToArray());
        Assert.IsTrue(result.LowArgs.ContextSeparatorEnabled);
        Assert.ContainsSingle(result.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, empty.Status);
        Assert.IsEmpty(empty.LowArgs!.ContextSeparator.ToArray());
        Assert.IsTrue(empty.LowArgs.ContextSeparatorEnabled);
        Assert.ContainsSingle(empty.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, literalInvalidEscape.Status);
        Assert.AreSequenceEqual<byte>([(byte)'\\', (byte)'x', (byte)'0'], literalInvalidEscape.LowArgs!.FieldMatchSeparator.ToArray());
        Assert.ContainsSingle(literalInvalidEscape.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies path separator flags accept separate, inline, empty, and escaped byte values.
    /// </summary>
    [TestMethod]
    public void ParsesPathSeparatorFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator"u8), OsString.FromUnixBytes("\\"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator=/"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult escaped = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator=\\0"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult empty = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator=Z"u8), OsString.FromUnixBytes("--path-separator="u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreEqual((byte)'\\', separate.LowArgs!.PathSeparator);
        Assert.ContainsSingle(separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreEqual((byte)'/', inline.LowArgs!.PathSeparator);
        Assert.ContainsSingle(inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, escaped.Status);
        Assert.AreEqual((byte)0, escaped.LowArgs!.PathSeparator);
        Assert.ContainsSingle(escaped.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, empty.Status);
        Assert.IsNull(empty.LowArgs!.PathSeparator);
        Assert.ContainsSingle(empty.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies context separator toggles use last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesContextSeparatorTogglePrecedence()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-context-separator"u8), OsString.FromUnixBytes("--context-separator=XX"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--context-separator=XX"u8), OsString.FromUnixBytes("--no-context-separator"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.ContextSeparatorEnabled);
        Assert.AreSequenceEqual("XX"u8.ToArray(), enabled.LowArgs.ContextSeparator.ToArray());
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.ContextSeparatorEnabled);
        Assert.AreSequenceEqual("XX"u8.ToArray(), disabled.LowArgs.ContextSeparator.ToArray());
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies printer separator diagnostics match ripgrep-style missing-value wording.
    /// </summary>
    [TestMethod]
    public void ReportsOutputSeparatorParseErrors()
    {
        CliParseResult missingField = CliParser.Parse([OsString.FromUnixBytes("--field-match-separator"u8)]);
        CliParseResult missingContext = CliParser.Parse([OsString.FromUnixBytes("--context-separator"u8)]);
        CliParseResult missingPath = CliParser.Parse([OsString.FromUnixBytes("--path-separator"u8)]);
        CliParseResult invalidPath = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator=foo"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missingField.Status);
        Assert.AreEqual(
            "missing value for flag --field-match-separator: missing argument for option '--field-match-separator'",
            missingField.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, missingContext.Status);
        Assert.AreEqual(
            "missing value for flag --context-separator: missing argument for option '--context-separator'",
            missingContext.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, missingPath.Status);
        Assert.AreEqual(
            "missing value for flag --path-separator: missing argument for option '--path-separator'",
            missingPath.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalidPath.Status);
        Assert.AreEqual(
            "error parsing flag --path-separator: A path separator must be exactly one byte, but the given separator is 3 bytes: foo\nIn some shells on Windows '/' is automatically expanded. Use '//' instead.",
            invalidPath.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-depth flags accept separate, inline and alias values with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesMaxDepthFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-d"u8), OsString.FromUnixBytes("1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-d1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult alias = CliParser.Parse(
            [OsString.FromUnixBytes("--max-depth"u8), OsString.FromUnixBytes("1"u8), OsString.FromUnixBytes("--maxdepth=2"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortSeparate.Status);
        Assert.AreEqual(1UL, shortSeparate.LowArgs!.MaxDepth);
        Assert.ContainsSingle(shortSeparate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortInline.Status);
        Assert.AreEqual(1UL, shortInline.LowArgs!.MaxDepth);
        Assert.ContainsSingle(shortInline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, alias.Status);
        Assert.AreEqual(2UL, alias.LowArgs!.MaxDepth);
        Assert.ContainsSingle(alias.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-depth parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsMaxDepthParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-depth"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-dx"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --max-depth: missing argument for option '--max-depth'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag -d: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-filesize flags accept byte values and uppercase binary suffixes.
    /// </summary>
    [TestMethod]
    public void ParsesMaxFileSizeFlags()
    {
        CliParseResult bytes = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize"u8), OsString.FromUnixBytes("4"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult kilobytes = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=2K"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult megabytes = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=3M"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult gigabytes = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=4G"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, bytes.Status);
        Assert.AreEqual(4UL, bytes.LowArgs!.MaxFileSize);
        Assert.ContainsSingle(bytes.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, kilobytes.Status);
        Assert.AreEqual(2048UL, kilobytes.LowArgs!.MaxFileSize);
        Assert.ContainsSingle(kilobytes.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, megabytes.Status);
        Assert.AreEqual(3UL * 1024UL * 1024UL, megabytes.LowArgs!.MaxFileSize);
        Assert.ContainsSingle(megabytes.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, gigabytes.Status);
        Assert.AreEqual(4UL * 1024UL * 1024UL * 1024UL, gigabytes.LowArgs!.MaxFileSize);
        Assert.ContainsSingle(gigabytes.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-filesize parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsMaxFileSizeParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-filesize"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--max-filesize=1k"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult overflow = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=18446744073709551616"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult suffixOverflow = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=9999999999999999G"u8), OsString.FromUnixBytes("needle"u8)]);
        byte[] invalidUtf8Value = [.. "--max-filesize="u8, 0xFF];
        CliParseResult invalidUtf8 = CliParser.Parse(
            [OsString.FromUnixBytes(invalidUtf8Value), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --max-filesize: missing argument for option '--max-filesize'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual(
            "error parsing flag --max-filesize: invalid size: invalid format for size '1k', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            invalid.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, overflow.Status);
        Assert.AreEqual(
            "error parsing flag --max-filesize: invalid size: invalid integer found in size '18446744073709551616': number too large to fit in target type",
            overflow.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, suffixOverflow.Status);
        Assert.AreEqual(
            "error parsing flag --max-filesize: invalid size: size too big in '9999999999999999G'",
            suffixOverflow.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalidUtf8.Status);
        Assert.AreEqual("error parsing flag --max-filesize: value is not valid UTF-8", invalidUtf8.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies glob flags accept separate and inline values.
    /// </summary>
    [TestMethod]
    public void ParsesGlobFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("-g"u8), OsString.FromUnixBytes("*.cs"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--glob=!*.log"u8), OsString.FromUnixBytes("-g*.txt"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--glob"u8), OsString.FromUnixBytes("-foo"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreSequenceEqual<CliGlobPattern>([new CliGlobPattern("*.cs", caseInsensitive: false)], separate.LowArgs!.GlobPatterns);
        Assert.ContainsSingle(separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreSequenceEqual<CliGlobPattern>(
            [new CliGlobPattern("!*.log", caseInsensitive: false), new CliGlobPattern("*.txt", caseInsensitive: false)],
            inline.LowArgs!.GlobPatterns);
        Assert.ContainsSingle(inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, dashValue.Status);
        Assert.AreSequenceEqual<CliGlobPattern>([new CliGlobPattern("-foo", caseInsensitive: false)], dashValue.LowArgs!.GlobPatterns);
        Assert.ContainsSingle(dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies case-insensitive glob flags and toggles are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesCaseInsensitiveGlobFlags()
    {
        CliParseResult insensitive = CliParser.Parse(
            [OsString.FromUnixBytes("--iglob"u8), OsString.FromUnixBytes("*.CS"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--iglob=*.TXT"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult toggle = CliParser.Parse(
            [
                OsString.FromUnixBytes("--glob-case-insensitive"u8),
                OsString.FromUnixBytes("-g"u8),
                OsString.FromUnixBytes("*.CS"u8),
                OsString.FromUnixBytes("--no-glob-case-insensitive"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Ok, insensitive.Status);
        Assert.AreSequenceEqual<CliGlobPattern>([new CliGlobPattern("*.CS", caseInsensitive: true)], insensitive.LowArgs!.GlobPatterns);
        Assert.ContainsSingle(insensitive.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreSequenceEqual<CliGlobPattern>([new CliGlobPattern("*.TXT", caseInsensitive: true)], inline.LowArgs!.GlobPatterns);
        Assert.ContainsSingle(inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, toggle.Status);
        Assert.IsFalse(toggle.LowArgs!.GlobCaseInsensitive);
        Assert.AreSequenceEqual<CliGlobPattern>([new CliGlobPattern("*.CS", caseInsensitive: false)], toggle.LowArgs.GlobPatterns);
        Assert.ContainsSingle(toggle.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file flags accept separate and inline values.
    /// </summary>
    [TestMethod]
    public void ParsesIgnoreFileFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file"u8), OsString.FromUnixBytes("first.ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file=second.ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file"u8), OsString.FromUnixBytes("-rules"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, separate.Status);
        Assert.AreSequenceEqual<string>(["first.ignore"], separate.LowArgs!.IgnoreFiles);
        Assert.ContainsSingle(separate.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, inline.Status);
        Assert.AreSequenceEqual<string>(["second.ignore"], inline.LowArgs!.IgnoreFiles);
        Assert.ContainsSingle(inline.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, dashValue.Status);
        Assert.AreSequenceEqual<string>(["-rules"], dashValue.LowArgs!.IgnoreFiles);
        Assert.ContainsSingle(dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file toggles use ripgrep's last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesIgnoreFilesToggles()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file=a"u8), OsString.FromUnixBytes("--no-ignore-files"u8), OsString.FromUnixBytes("--ignore-file=b"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-ignore-files"u8), OsString.FromUnixBytes("--ignore-file=a"u8), OsString.FromUnixBytes("--ignore-files"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.RespectExplicitIgnoreFiles);
        Assert.AreSequenceEqual<string>(["a", "b"], disabled.LowArgs.IgnoreFiles);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.RespectExplicitIgnoreFiles);
        Assert.AreSequenceEqual<string>(["a"], enabled.LowArgs.IgnoreFiles);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsIgnoreFileParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--ignore-file"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --ignore-file: missing argument for option '--ignore-file'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies iglob parser diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsInsensitiveGlobParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--iglob"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --iglob: missing argument for option '--iglob'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies sort flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesSortFlags()
    {
        CliParseResult ascending = CliParser.Parse(
            [OsString.FromUnixBytes("--sort"u8), OsString.FromUnixBytes("path"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult descending = CliParser.Parse(
            [OsString.FromUnixBytes("--sort=path"u8), OsString.FromUnixBytes("--sortr=modified"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sortr"u8), OsString.FromUnixBytes("created"u8), OsString.FromUnixBytes("--sort=none"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, ascending.Status);
        Assert.IsNotNull(ascending.LowArgs!.SortMode);
        Assert.IsFalse(ascending.LowArgs.SortMode.Value.Reverse);
        Assert.AreEqual(CliSortKind.Path, ascending.LowArgs.SortMode.Value.Kind);
        Assert.ContainsSingle(ascending.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, descending.Status);
        Assert.IsNotNull(descending.LowArgs!.SortMode);
        Assert.IsTrue(descending.LowArgs.SortMode.Value.Reverse);
        Assert.AreEqual(CliSortKind.LastModified, descending.LowArgs.SortMode.Value.Kind);
        Assert.ContainsSingle(descending.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsNull(disabled.LowArgs!.SortMode);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies deprecated sort-files flags map to path sorting and can be disabled.
    /// </summary>
    [TestMethod]
    public void ParsesSortFilesFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sort=created"u8), OsString.FromUnixBytes("--sort-files"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sort-files"u8), OsString.FromUnixBytes("--no-sort-files"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsNotNull(enabled.LowArgs!.SortMode);
        Assert.IsFalse(enabled.LowArgs.SortMode.Value.Reverse);
        Assert.AreEqual(CliSortKind.Path, enabled.LowArgs.SortMode.Value.Kind);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsNull(disabled.LowArgs!.SortMode);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies sort parse errors use ripgrep-style wording.
    /// </summary>
    [TestMethod]
    public void ReportsSortParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--sort"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--sortr=bogus"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missing.Status);
        Assert.AreEqual("missing value for flag --sort: missing argument for option '--sort'", missing.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, invalid.Status);
        Assert.AreEqual("error parsing flag --sortr: choice 'bogus' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies file type selection flags accept separate and inline values.
    /// </summary>
    [TestMethod]
    public void ParsesTypeSelectionFlags()
    {
        CliParseResult result = CliParser.Parse(
            [
                OsString.FromUnixBytes("-tcs"u8),
                OsString.FromUnixBytes("--type"u8),
                OsString.FromUnixBytes("txt"u8),
                OsString.FromUnixBytes("-Tjson"u8),
                OsString.FromUnixBytes("--type-not=xml"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.AreSequenceEqual<CliTypeChange>(
            [
                new CliTypeChange(CliTypeChangeKind.Select, "cs"),
                new CliTypeChange(CliTypeChangeKind.Select, "txt"),
                new CliTypeChange(CliTypeChangeKind.Negate, "json"),
                new CliTypeChange(CliTypeChangeKind.Negate, "xml"),
            ],
            result.LowArgs!.TypeChanges);
        Assert.ContainsSingle(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies file type definition flags preserve ordered changes.
    /// </summary>
    [TestMethod]
    public void ParsesTypeDefinitionFlags()
    {
        CliParseResult result = CliParser.Parse(
            [
                OsString.FromUnixBytes("--type-clear=foo"u8),
                OsString.FromUnixBytes("--type-add"u8),
                OsString.FromUnixBytes("foo:*.foo"u8),
                OsString.FromUnixBytes("--type-list"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.IsTrue(result.LowArgs!.TypeList);
        Assert.AreSequenceEqual<CliTypeChange>(
            [
                new CliTypeChange(CliTypeChangeKind.Clear, "foo"),
                new CliTypeChange(CliTypeChangeKind.Add, "foo:*.foo"),
            ],
            result.LowArgs.TypeChanges);
        Assert.IsEmpty(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies type parser missing-value diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
    public void ReportsTypeParseErrors()
    {
        CliParseResult missingType = CliParser.Parse([OsString.FromUnixBytes("-t"u8)]);
        CliParseResult missingTypeAdd = CliParser.Parse([OsString.FromUnixBytes("--type-add"u8)]);

        Assert.AreEqual(CliParseStatus.Error, missingType.Status);
        Assert.AreEqual("missing value for flag -t: missing argument for option '-t'", missingType.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, missingTypeAdd.Status);
        Assert.AreEqual("missing value for flag --type-add: missing argument for option '--type-add'", missingTypeAdd.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies trim flags use last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesTrimFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-trim"u8), OsString.FromUnixBytes("--trim"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--trim"u8), OsString.FromUnixBytes("--no-trim"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Trim);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Trim);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies heading flags use last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesHeadingFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("--heading"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--heading"u8), OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Heading);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Heading);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies files mode treats positional arguments as paths.
    /// </summary>
    [TestMethod]
    public void ParsesFilesMode()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("src"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.AreEqual(CliSearchMode.Files, result.LowArgs!.SearchMode);
        Assert.ContainsSingle(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies file-list mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesFileListModeFlags()
    {
        CliParseResult withMatches = CliParser.Parse(
            [OsString.FromUnixBytes("--files-without-match"u8), OsString.FromUnixBytes("-l"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult withoutMatch = CliParser.Parse(
            [OsString.FromUnixBytes("--files-with-matches"u8), OsString.FromUnixBytes("--files-without-match"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult filesThenWithMatches = CliParser.Parse(
            [OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("-l"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult withMatchesThenFiles = CliParser.Parse(
            [OsString.FromUnixBytes("-l"u8), OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("src"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, withMatches.Status);
        Assert.AreEqual(CliSearchMode.FilesWithMatches, withMatches.LowArgs!.SearchMode);
        Assert.ContainsSingle(withMatches.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, withoutMatch.Status);
        Assert.AreEqual(CliSearchMode.FilesWithoutMatch, withoutMatch.LowArgs!.SearchMode);
        Assert.ContainsSingle(withoutMatch.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, filesThenWithMatches.Status);
        Assert.AreEqual(CliSearchMode.FilesWithMatches, filesThenWithMatches.LowArgs!.SearchMode);
        Assert.ContainsSingle(filesThenWithMatches.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, withMatchesThenFiles.Status);
        Assert.AreEqual(CliSearchMode.Files, withMatchesThenFiles.LowArgs!.SearchMode);
        Assert.ContainsSingle(withMatchesThenFiles.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies JSON mode flags follow ripgrep mode precedence.
    /// </summary>
    [TestMethod]
    public void ParsesJsonModeFlags()
    {
        CliParseResult json = CliParser.Parse(
            [OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("--no-json"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult countThenJson = CliParser.Parse(
            [OsString.FromUnixBytes("-c"u8), OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult jsonThenCount = CliParser.Parse(
            [OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("-c"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult filesThenJson = CliParser.Parse(
            [OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("src"u8)]);
        CliParseResult jsonThenFiles = CliParser.Parse(
            [OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("src"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, json.Status);
        Assert.AreEqual(CliSearchMode.Json, json.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.AreEqual(CliSearchMode.Standard, disabled.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, countThenJson.Status);
        Assert.AreEqual(CliSearchMode.Json, countThenJson.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, jsonThenCount.Status);
        Assert.AreEqual(CliSearchMode.Count, jsonThenCount.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, filesThenJson.Status);
        Assert.AreEqual(CliSearchMode.Json, filesThenJson.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, jsonThenFiles.Status);
        Assert.AreEqual(CliSearchMode.Files, jsonThenFiles.LowArgs!.SearchMode);
    }

    /// <summary>
    /// Verifies fixed-string mode flags are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesFixedStringsFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-F"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--fixed-strings"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.FixedStrings);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.FixedStrings);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies quiet mode flags are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesQuietFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-q"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--quiet"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.Quiet);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.Quiet);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies stats flags use last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesStatsFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-stats"u8), OsString.FromUnixBytes("--stats"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--stats"u8), OsString.FromUnixBytes("--no-stats"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.Stats);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.Stats);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies only-matching flags are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesOnlyMatchingFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-o"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--only-matching"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.OnlyMatching);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.OnlyMatching);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies vimgrep output mode is parsed.
    /// </summary>
    [TestMethod]
    public void ParsesVimgrepFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--vimgrep"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.IsTrue(result.LowArgs!.Vimgrep);
        Assert.ContainsSingle(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies null path terminator flags are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesNullPathTerminatorFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-0"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--null"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.NullPathTerminator);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.NullPathTerminator);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies invert-match flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesInvertMatchFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--invert-match"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-v"u8), OsString.FromUnixBytes("--no-invert-match"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.InvertMatch);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.InvertMatch);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies line-regexp flags are parsed.
    /// </summary>
    [TestMethod]
    public void ParsesLineRegexpFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-x"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--line-regexp"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.LineRegexp);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.LineRegexp);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies word-regexp flags are parsed and use last-wins behavior with line-regexp.
    /// </summary>
    [TestMethod]
    public void ParsesWordRegexpFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-w"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--word-regexp"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult wordWins = CliParser.Parse(
            [OsString.FromUnixBytes("-x"u8), OsString.FromUnixBytes("-w"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult lineWins = CliParser.Parse(
            [OsString.FromUnixBytes("-w"u8), OsString.FromUnixBytes("-x"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortFlag.Status);
        Assert.IsTrue(shortFlag.LowArgs!.WordRegexp);
        Assert.IsFalse(shortFlag.LowArgs.LineRegexp);
        Assert.ContainsSingle(shortFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longFlag.Status);
        Assert.IsTrue(longFlag.LowArgs!.WordRegexp);
        Assert.IsFalse(longFlag.LowArgs.LineRegexp);
        Assert.ContainsSingle(longFlag.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, wordWins.Status);
        Assert.IsTrue(wordWins.LowArgs!.WordRegexp);
        Assert.IsFalse(wordWins.LowArgs.LineRegexp);
        Assert.ContainsSingle(wordWins.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, lineWins.Status);
        Assert.IsFalse(lineWins.LowArgs!.WordRegexp);
        Assert.IsTrue(lineWins.LowArgs.LineRegexp);
        Assert.ContainsSingle(lineWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies filename-prefix flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesFilenameFlags()
    {
        CliParseResult shortWith = CliParser.Parse(
            [OsString.FromUnixBytes("-H"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longWith = CliParser.Parse(
            [OsString.FromUnixBytes("--with-filename"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortWithout = CliParser.Parse(
            [OsString.FromUnixBytes("-I"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult withoutWins = CliParser.Parse(
            [OsString.FromUnixBytes("--with-filename"u8), OsString.FromUnixBytes("--no-filename"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult withWins = CliParser.Parse(
            [OsString.FromUnixBytes("--no-filename"u8), OsString.FromUnixBytes("-H"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, shortWith.Status);
        Assert.IsTrue(shortWith.LowArgs!.WithFilename);
        Assert.ContainsSingle(shortWith.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, longWith.Status);
        Assert.IsTrue(longWith.LowArgs!.WithFilename);
        Assert.ContainsSingle(longWith.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, shortWithout.Status);
        Assert.IsFalse(shortWithout.LowArgs!.WithFilename);
        Assert.ContainsSingle(shortWithout.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, withoutWins.Status);
        Assert.IsFalse(withoutWins.LowArgs!.WithFilename);
        Assert.ContainsSingle(withoutWins.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, withWins.Status);
        Assert.IsTrue(withWins.LowArgs!.WithFilename);
        Assert.ContainsSingle(withWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies case mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [TestMethod]
    public void ParsesCaseModeFlags()
    {
        CliParseResult sensitive = CliParser.Parse(
            [OsString.FromUnixBytes("-i"u8), OsString.FromUnixBytes("-s"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult insensitive = CliParser.Parse(
            [OsString.FromUnixBytes("-s"u8), OsString.FromUnixBytes("--ignore-case"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult smart = CliParser.Parse(
            [OsString.FromUnixBytes("--smart-case"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, sensitive.Status);
        Assert.AreEqual(CliCaseMode.Sensitive, sensitive.LowArgs!.CaseMode);
        Assert.ContainsSingle(sensitive.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, insensitive.Status);
        Assert.AreEqual(CliCaseMode.Insensitive, insensitive.LowArgs!.CaseMode);
        Assert.ContainsSingle(insensitive.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, smart.Status);
        Assert.AreEqual(CliCaseMode.Smart, smart.LowArgs!.CaseMode);
        Assert.ContainsSingle(smart.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies hidden traversal flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesHiddenFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-hidden"u8), OsString.FromUnixBytes("--hidden"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-."u8), OsString.FromUnixBytes("--no-hidden"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.IncludeHidden);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.IncludeHidden);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies follow flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesFollowFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-follow"u8), OsString.FromUnixBytes("-L"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--follow"u8), OsString.FromUnixBytes("--no-follow"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.FollowLinks);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.FollowLinks);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies ignore-file flags are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesIgnoreFlags()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore"u8), OsString.FromUnixBytes("--no-ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-ignore"u8), OsString.FromUnixBytes("--ignore"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, disabled.Status);
        Assert.IsFalse(disabled.LowArgs!.RespectIgnoreFiles);
        Assert.ContainsSingle(disabled.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, enabled.Status);
        Assert.IsTrue(enabled.LowArgs!.RespectIgnoreFiles);
        Assert.ContainsSingle(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies ignore source flags are parsed independently with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesIgnoreSourceFlags()
    {
        CliParseResult result = CliParser.Parse(
            [
                OsString.FromUnixBytes("--no-ignore"u8),
                OsString.FromUnixBytes("--ignore-dot"u8),
                OsString.FromUnixBytes("--ignore-vcs"u8),
                OsString.FromUnixBytes("--no-ignore-exclude"u8),
                OsString.FromUnixBytes("--no-ignore-global"u8),
                OsString.FromUnixBytes("--ignore-messages"u8),
                OsString.FromUnixBytes("--ignore-parent"u8),
                OsString.FromUnixBytes("--no-ignore-messages"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        Assert.IsFalse(result.LowArgs!.RespectIgnoreFiles);
        Assert.IsTrue(result.LowArgs.RespectDotIgnoreFiles);
        Assert.IsTrue(result.LowArgs.RespectGitIgnoreFiles);
        Assert.IsFalse(result.LowArgs.RespectGitExcludeFiles);
        Assert.IsFalse(result.LowArgs.RespectGlobalIgnoreFiles);
        Assert.IsTrue(result.LowArgs.RespectParentIgnoreFiles);
        Assert.IsFalse(result.LowArgs.IgnoreMessages);
        Assert.ContainsSingle(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies traversal ignore modifiers are parsed with last-wins behavior.
    /// </summary>
    [TestMethod]
    public void ParsesTraversalIgnoreModifiers()
    {
        CliParseResult requireGit = CliParser.Parse(
            [OsString.FromUnixBytes("--no-require-git"u8), OsString.FromUnixBytes("--require-git"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult ignoreCase = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file-case-insensitive"u8), OsString.FromUnixBytes("--no-ignore-file-case-insensitive"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult fileSystem = CliParser.Parse(
            [OsString.FromUnixBytes("--no-one-file-system"u8), OsString.FromUnixBytes("--one-file-system"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.AreEqual(CliParseStatus.Ok, requireGit.Status);
        Assert.IsTrue(requireGit.LowArgs!.RequireGitRepository);
        Assert.ContainsSingle(requireGit.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, ignoreCase.Status);
        Assert.IsFalse(ignoreCase.LowArgs!.IgnoreFileCaseInsensitive);
        Assert.ContainsSingle(ignoreCase.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, fileSystem.Status);
        Assert.IsTrue(fileSystem.LowArgs!.OneFileSystem);
        Assert.ContainsSingle(fileSystem.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrecognized long flags match ripgrep's low-level message.
    /// </summary>
    [TestMethod]
    public void ReportsUnrecognizedLongFlag()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("--bogus"u8)]);

        Assert.AreEqual(CliParseStatus.Error, result.Status);
        Assert.AreEqual("unrecognized flag --bogus", result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies invalid UTF-8 in flag names is rejected before text replacement can occur.
    /// </summary>
    [TestMethod]
    public void RejectsInvalidUtf8FlagName()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes([0x2d, 0x2d, 0xff])]);

        Assert.AreEqual(CliParseStatus.Error, result.Status);
        Assert.AreEqual("invalid CLI arguments", result.Error!.FormatAlternate());
    }
}
