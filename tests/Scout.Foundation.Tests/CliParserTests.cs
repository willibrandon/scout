
using System;

namespace Scout;

/// <summary>
/// Verifies low-level CLI parsing behavior.
/// </summary>
public sealed class CliParserTests
{
    /// <summary>
    /// Verifies <c>-V</c> selects the short version special mode.
    /// </summary>
    [Fact]
    public void ParsesShortVersionSpecialMode()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("-V"u8)]);

        Assert.Equal(CliParseStatus.Special, result.Status);
        Assert.Equal(CliSpecialMode.VersionShort, result.SpecialMode);
    }

    /// <summary>
    /// Verifies <c>--version</c> selects the long version special mode.
    /// </summary>
    [Fact]
    public void ParsesLongVersionSpecialMode()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("--version"u8)]);

        Assert.Equal(CliParseStatus.Special, result.Status);
        Assert.Equal(CliSpecialMode.VersionLong, result.SpecialMode);
    }

    /// <summary>
    /// Verifies positional arguments are retained as operating-system strings.
    /// </summary>
    [Fact]
    public void PreservesPositionalArguments()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes([0xff, 0x80])]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.Single(result.LowArgs!.Positional);
        Assert.Equal([0xff, 0x80], result.LowArgs.Positional[0].AsUnixBytes().ToArray());
    }

    /// <summary>
    /// Verifies explicit regexp flags are parsed independently from positional paths.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal([OsString.FromUnixBytes("needle"u8)], separate.LowArgs!.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal([OsString.FromUnixBytes("alpha"u8), OsString.FromUnixBytes("beta"u8)], inline.LowArgs!.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, empty.Status);
        Assert.Equal([OsString.FromUnixBytes(""u8)], empty.LowArgs!.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], empty.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, dashValue.Status);
        Assert.Equal([OsString.FromUnixBytes("-needle"u8)], dashValue.LowArgs!.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies combined short flags are parsed with ripgrep-compatible value consumption.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, switches.Status);
        Assert.True(switches.LowArgs!.LineNumber);
        Assert.True(switches.LowArgs.WithFilename);
        Assert.True(switches.LowArgs.InvertMatch);
        Assert.Equal(CliCaseMode.Insensitive, switches.LowArgs.CaseMode);
        Assert.Equal([OsString.FromUnixBytes("needle"u8)], switches.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inlineValue.Status);
        Assert.True(inlineValue.LowArgs!.LineNumber);
        Assert.Equal(2UL, inlineValue.LowArgs.AfterContext);
        Assert.Equal(CliParseStatus.Ok, followingValue.Status);
        Assert.True(followingValue.LowArgs!.LineNumber);
        Assert.Equal([OsString.FromUnixBytes("needle"u8)], followingValue.LowArgs.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], followingValue.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, unrestricted.Status);
        Assert.Equal(3, unrestricted.LowArgs!.UnrestrictedCount);
        Assert.True(unrestricted.LowArgs.LineNumber);
        Assert.Equal(CliParseStatus.Error, invalidValue.Status);
        Assert.Equal("error parsing flag -m: value is not a valid number: invalid digit found in string", invalidValue.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, unknown.Status);
        Assert.Equal("unrecognized flag -y", unknown.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies explicit pattern-file flags are parsed as ordered pattern sources.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal([CliPatternSource.File(OsString.FromUnixBytes("patterns.txt"u8))], separate.LowArgs!.PatternSources);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal(
            [
                CliPatternSource.File(OsString.FromUnixBytes("first.txt"u8)),
                CliPatternSource.File(OsString.FromUnixBytes("second.txt"u8)),
            ],
            inline.LowArgs!.PatternSources);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, ordered.Status);
        Assert.Equal(
            [
                CliPatternSource.Pattern(OsString.FromUnixBytes("alpha"u8)),
                CliPatternSource.File(OsString.FromUnixBytes("patterns.txt"u8)),
            ],
            ordered.LowArgs!.PatternSources);
        Assert.Equal([OsString.FromUnixBytes("alpha"u8)], ordered.LowArgs.Patterns);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], ordered.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, dashValue.Status);
        Assert.Equal([CliPatternSource.File(OsString.FromUnixBytes("-patterns"u8))], dashValue.LowArgs!.PatternSources);
        Assert.Equal([OsString.FromUnixBytes("path.txt"u8)], dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies regexp parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsRegexpParseErrors()
    {
        CliParseResult shortMissing = CliParser.Parse([OsString.FromUnixBytes("-e"u8)]);
        CliParseResult longMissing = CliParser.Parse([OsString.FromUnixBytes("--regexp"u8)]);

        Assert.Equal(CliParseStatus.Error, shortMissing.Status);
        Assert.Equal("missing value for flag -e: missing argument for option '-e'", shortMissing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, longMissing.Status);
        Assert.Equal("missing value for flag --regexp: missing argument for option '--regexp'", longMissing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies pattern-file parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsPatternFileParseErrors()
    {
        CliParseResult shortMissing = CliParser.Parse([OsString.FromUnixBytes("-f"u8)]);
        CliParseResult longMissing = CliParser.Parse([OsString.FromUnixBytes("--file"u8)]);

        Assert.Equal(CliParseStatus.Error, shortMissing.Status);
        Assert.Equal("missing value for flag -f: missing argument for option '-f'", shortMissing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, longMissing.Status);
        Assert.Equal("missing value for flag --file: missing argument for option '--file'", longMissing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies <c>--no-config</c> is accepted as a no-op after startup expansion.
    /// </summary>
    [Fact]
    public void ParsesNoConfigFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--no-config"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.Equal([OsString.FromUnixBytes("needle"u8)], result.LowArgs!.Positional);
    }

    /// <summary>
    /// Verifies <c>--generate</c> selects generated artifact modes with ripgrep's mode override behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, man.Status);
        Assert.Equal(CliGenerateMode.Man, man.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, bash.Status);
        Assert.Equal(CliGenerateMode.CompleteBash, bash.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, zsh.Status);
        Assert.Equal(CliGenerateMode.CompleteZsh, zsh.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, fish.Status);
        Assert.Equal(CliGenerateMode.CompleteFish, fish.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, powershell.Status);
        Assert.Equal(CliGenerateMode.CompletePowerShell, powershell.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, lastGenerateWins.Status);
        Assert.Equal(CliGenerateMode.Man, lastGenerateWins.LowArgs!.GenerateMode);
        Assert.Equal(CliParseStatus.Ok, searchWins.Status);
        Assert.Null(searchWins.LowArgs!.GenerateMode);
        Assert.Equal(CliSearchMode.FilesWithMatches, searchWins.LowArgs.SearchMode);
        Assert.Equal(CliParseStatus.Ok, jsonResetWins.Status);
        Assert.Null(jsonResetWins.LowArgs!.GenerateMode);
        Assert.Equal(CliSearchMode.Standard, jsonResetWins.LowArgs.SearchMode);
    }

    /// <summary>
    /// Verifies <c>--generate</c> parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsGenerateParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--generate"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--generate=foo"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --generate: missing argument for option '--generate'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --generate: choice 'foo' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies remaining non-generate upstream flags are represented in low arguments.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, search.Status);
        Assert.False(search.LowArgs!.FixedStrings);
        Assert.Equal(9UL * 1024UL * 1024UL * 1024UL, search.LowArgs.DfaSizeLimit);
        Assert.Equal(2UL * 1024UL * 1024UL, search.LowArgs.RegexSizeLimit);
        Assert.Equal(["match:fg:magenta", "line:bg:yellow"], search.LowArgs.ColorSpecs);
        Assert.Equal("hostname", search.LowArgs.HostnameBin);
        Assert.Equal("file://{host}{path}", search.LowArgs.HyperlinkFormat);
        Assert.False(search.LowArgs.StopOnNonmatch);
        Assert.False(search.LowArgs.Multiline);
        Assert.False(search.LowArgs.MultilineDotall);
        Assert.True(search.LowArgs.Unicode);
        Assert.Single(search.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, traceWins.Status);
        Assert.Equal(CliLoggingMode.Trace, traceWins.LowArgs!.LoggingMode);
        Assert.Equal(CliParseStatus.Ok, debugWins.Status);
        Assert.Equal(CliLoggingMode.Debug, debugWins.LowArgs!.LoggingMode);
    }

    /// <summary>
    /// Verifies invalid <c>--colors</c> values report ripgrep-compatible parser diagnostics.
    /// </summary>
    /// <param name="spec">The invalid color specification.</param>
    /// <param name="expected">The expected diagnostic text.</param>
    [Theory]
    [InlineData("bad", "error parsing flag --colors: invalid color spec format: 'bad'. Valid format is '(path|line|column|match|highlight):(fg|bg|style):(value)'.")]
    [InlineData("foo:fg:red", "error parsing flag --colors: unrecognized output type 'foo'. Choose from: path, line, column, match, highlight.")]
    [InlineData("match:what:red", "error parsing flag --colors: unrecognized spec type 'what'. Choose from: fg, bg, style, none.")]
    [InlineData("match:fg:bogus", "error parsing flag --colors: unrecognized color name 'bogus'. Choose from: black, blue, green, red, cyan, magenta, yellow, white")]
    [InlineData("match:style:bad", "error parsing flag --colors: unrecognized style attribute 'bad'. Choose from: nobold, bold, nointense, intense, nounderline, underline, noitalic, italic.")]
    [InlineData("match:fg", "error parsing flag --colors: invalid color spec format: 'match:fg'. Valid format is '(path|line|column|match|highlight):(fg|bg|style):(value)'.")]
    [InlineData("match:fg:999", "error parsing flag --colors: unrecognized ansi256 color number, should be '[0-255]' (or a hex number), but is '999'")]
    [InlineData("match:fg:1,2", "error parsing flag --colors: unrecognized RGB color triple, should be '[0-255],[0-255],[0-255]' (or a hex triple), but is '1,2'")]
    public void ReportsColorSpecParseErrors(string spec, string expected)
    {
        CliParseResult result = CliParser.Parse([OsString.FromText("--colors"), OsString.FromText(spec), OsString.FromText("needle")]);

        Assert.Equal(CliParseStatus.Error, result.Status);
        Assert.Equal(expected, result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies <c>--hyperlink-format</c> aliases normalize like ripgrep.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, none.Status);
        Assert.Equal(string.Empty, none.LowArgs!.HyperlinkFormat);
        Assert.Equal(CliParseStatus.Ok, defaultAlias.Status);
        Assert.Equal(expectedDefault, defaultAlias.LowArgs!.HyperlinkFormat);
        Assert.Equal(CliParseStatus.Ok, file.Status);
        Assert.Equal("file://{host}{path}", file.LowArgs!.HyperlinkFormat);
        Assert.Equal(CliParseStatus.Ok, lastWins.Status);
        Assert.Equal("grep+://{path}:{line}", lastWins.LowArgs!.HyperlinkFormat);
    }

    /// <summary>
    /// Verifies invalid <c>--hyperlink-format</c> values report ripgrep-compatible parser diagnostics.
    /// </summary>
    /// <param name="format">The invalid hyperlink format.</param>
    /// <param name="expected">The expected diagnostic text.</param>
    [Theory]
    [InlineData("foo://bar", "error parsing flag --hyperlink-format: invalid hyperlink format: at least a {path} variable is required in a hyperlink format, or otherwise use a valid alias: default, none, cursor, file, grep+, kitty, macvim, textmate, vscode, vscode-insiders, vscodium")]
    [InlineData("foo://{line}", "error parsing flag --hyperlink-format: invalid hyperlink format: the {path} variable is required in a hyperlink format")]
    [InlineData("foo://{path", "error parsing flag --hyperlink-format: invalid hyperlink format: unclosed variable: found '{' without a corresponding '}' following it")]
    [InlineData("foo://{path}:{column}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format contains a {column} variable, but no {line} variable is present")]
    [InlineData("{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [InlineData(":{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [InlineData("f*:{path}", "error parsing flag --hyperlink-format: invalid hyperlink format: the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:")]
    [InlineData("foo://{bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: invalid hyperlink format variable: 'bar', choose from: path, line, column, host, wslprefix")]
    [InlineData("foo://{}}bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: invalid hyperlink format variable: '', choose from: path, line, column, host, wslprefix")]
    [InlineData("foo://{{bar}", "error parsing flag --hyperlink-format: invalid hyperlink format: unopened variable: found '}' without a corresponding '{' preceding it")]
    public void ReportsHyperlinkFormatParseErrors(string format, string expected)
    {
        CliParseResult result = CliParser.Parse([OsString.FromText("--hyperlink-format"), OsString.FromText(format), OsString.FromText("needle")]);

        Assert.Equal(CliParseStatus.Error, result.Status);
        Assert.Equal(expected, result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies binary text-mode flags are parsed with ripgrep's order-sensitive behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, enabledShort.Status);
        Assert.True(enabledShort.LowArgs!.TextMode);
        Assert.Equal(CliParseStatus.Ok, enabledLong.Status);
        Assert.True(enabledLong.LowArgs!.TextMode);
        Assert.Equal(CliParseStatus.Ok, binaryWins.Status);
        Assert.False(binaryWins.LowArgs!.TextMode);
        Assert.Equal(CliParseStatus.Ok, textWins.Status);
        Assert.True(textWins.LowArgs!.TextMode);
        Assert.Equal(CliParseStatus.Ok, noText.Status);
        Assert.False(noText.LowArgs!.TextMode);
        Assert.Equal(CliParseStatus.Ok, noBinary.Status);
        Assert.False(noBinary.LowArgs!.TextMode);
    }

    /// <summary>
    /// Verifies preprocessing and compressed-search flags use ripgrep's override behavior.
    /// </summary>
    [Fact]
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
        CliParseResult zipOverridesPre = CliParser.Parse(
            [OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("-z"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult preOverridesZip = CliParser.Parse(
            [OsString.FromUnixBytes("-z"u8), OsString.FromUnixBytes("--pre=cat"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult preGlob = CliParser.Parse(
            [OsString.FromUnixBytes("--pre-glob"u8), OsString.FromUnixBytes("*.xz"u8), OsString.FromUnixBytes("--pre-glob=*.gz"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, zip.Status);
        Assert.True(zip.LowArgs!.SearchZip);
        Assert.Null(zip.LowArgs.Preprocessor);
        Assert.Equal(CliParseStatus.Ok, noZip.Status);
        Assert.False(noZip.LowArgs!.SearchZip);
        Assert.Equal(CliParseStatus.Ok, pre.Status);
        Assert.Equal("cat", pre.LowArgs!.Preprocessor);
        Assert.False(pre.LowArgs.SearchZip);
        Assert.Equal(CliParseStatus.Ok, emptyPre.Status);
        Assert.Null(emptyPre.LowArgs!.Preprocessor);
        Assert.Equal(CliParseStatus.Ok, zipOverridesPre.Status);
        Assert.True(zipOverridesPre.LowArgs!.SearchZip);
        Assert.Null(zipOverridesPre.LowArgs.Preprocessor);
        Assert.Equal(CliParseStatus.Ok, preOverridesZip.Status);
        Assert.False(preOverridesZip.LowArgs!.SearchZip);
        Assert.Equal("cat", preOverridesZip.LowArgs.Preprocessor);
        Assert.Equal(CliParseStatus.Ok, preGlob.Status);
        Assert.Equal(["*.xz", "*.gz"], preGlob.LowArgs!.PreprocessorGlobs);
    }

    /// <summary>
    /// Verifies thread-count flags accept ripgrep's separate and inline value forms.
    /// </summary>
    [Fact]
    public void ParsesThreadsFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--threads"u8), OsString.FromUnixBytes("2"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult zero = CliParser.Parse(
            [OsString.FromUnixBytes("--threads=0"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--threads=0"u8), OsString.FromUnixBytes("-j2"u8), OsString.FromUnixBytes("-j=4"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal(2UL, separate.LowArgs!.Threads);
        Assert.Single(separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, zero.Status);
        Assert.Null(zero.LowArgs!.Threads);
        Assert.Single(zero.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal(4UL, inline.LowArgs!.Threads);
        Assert.Single(inline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies thread-count diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsThreadsParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--threads"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--threads=abc"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInvalid = CliParser.Parse([OsString.FromUnixBytes("-j"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --threads: missing argument for option '--threads'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --threads: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, shortInvalid.Status);
        Assert.Equal("error parsing flag -j: value is not a valid number: invalid digit found in string", shortInvalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies buffering and memory-map switches use ripgrep's mode-setting behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, defaultModes.Status);
        Assert.Equal(CliBufferMode.Auto, defaultModes.LowArgs!.BufferMode);
        Assert.Equal(CliMmapMode.Auto, defaultModes.LowArgs.MmapMode);
        Assert.Equal(CliParseStatus.Ok, buffered.Status);
        Assert.Equal(CliBufferMode.Auto, buffered.LowArgs!.BufferMode);
        Assert.Single(buffered.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, mmap.Status);
        Assert.Equal(CliMmapMode.AlwaysTryMmap, mmap.LowArgs!.MmapMode);
        Assert.Single(mmap.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies message switches use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesMessageFlags()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--messages"u8), OsString.FromUnixBytes("--no-messages"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-messages"u8), OsString.FromUnixBytes("--messages"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Messages);
        Assert.Single(disabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Messages);
        Assert.Single(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies stop-on-nonmatch is parsed as a search switch.
    /// </summary>
    [Fact]
    public void ParsesStopOnNonmatchFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--stop-on-nonmatch"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.True(result.LowArgs!.StopOnNonmatch);
        Assert.Single(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies CRLF mode flags use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Crlf);
        Assert.False(enabled.LowArgs.NullData);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Crlf);
        Assert.False(disabled.LowArgs.NullData);
        Assert.Single(disabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, nullData.Status);
        Assert.True(nullData.LowArgs!.NullData);
        Assert.False(nullData.LowArgs.Crlf);
        Assert.Single(nullData.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, crlfThenNullData.Status);
        Assert.True(crlfThenNullData.LowArgs!.NullData);
        Assert.True(crlfThenNullData.LowArgs.Crlf);
        Assert.Single(crlfThenNullData.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, nullDataThenCrlf.Status);
        Assert.True(nullDataThenCrlf.LowArgs!.Crlf);
        Assert.False(nullDataThenCrlf.LowArgs.NullData);
        Assert.Single(nullDataThenCrlf.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, nullDataThenNoCrlf.Status);
        Assert.True(nullDataThenNoCrlf.LowArgs!.NullData);
        Assert.False(nullDataThenNoCrlf.LowArgs.Crlf);
        Assert.Single(nullDataThenNoCrlf.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies regex engine switches use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, pcre2.Status);
        Assert.Equal(CliRegexEngine.Pcre2, pcre2.LowArgs!.RegexEngine);
        Assert.Single(pcre2.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, defaultEngine.Status);
        Assert.Equal(CliRegexEngine.Default, defaultEngine.LowArgs!.RegexEngine);
        Assert.Single(defaultEngine.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, hybrid.Status);
        Assert.False(hybrid.LowArgs!.AutoHybridRegex);
        Assert.Equal(CliRegexEngine.Default, hybrid.LowArgs.RegexEngine);
        Assert.Single(hybrid.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, pcre2Unicode.Status);
        Assert.True(pcre2Unicode.LowArgs!.Pcre2Unicode);
        Assert.Single(pcre2Unicode.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, pcre2ThenHybrid.Status);
        Assert.Equal(CliRegexEngine.Auto, pcre2ThenHybrid.LowArgs!.RegexEngine);
        Assert.Equal(CliParseStatus.Ok, hybridThenPcre2.Status);
        Assert.Equal(CliRegexEngine.Pcre2, hybridThenPcre2.LowArgs!.RegexEngine);
        Assert.Equal(CliParseStatus.Ok, pcre2ThenNoHybrid.Status);
        Assert.Equal(CliRegexEngine.Default, pcre2ThenNoHybrid.LowArgs!.RegexEngine);
    }

    /// <summary>
    /// Verifies regex engine parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsRegexEngineParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--engine"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--engine=bogus"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --engine: missing argument for option '--engine'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --engine: unrecognized regex engine 'bogus'", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies encoding flags use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, none.Status);
        Assert.Equal(CliEncodingMode.None, none.LowArgs!.EncodingMode);
        Assert.Single(none.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inlineNone.Status);
        Assert.Equal(CliEncodingMode.None, inlineNone.LowArgs!.EncodingMode);
        Assert.Single(inlineNone.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortNone.Status);
        Assert.Equal(CliEncodingMode.None, shortNone.LowArgs!.EncodingMode);
        Assert.Single(shortNone.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, noEncoding.Status);
        Assert.Equal(CliEncodingMode.Auto, noEncoding.LowArgs!.EncodingMode);
        Assert.Single(noEncoding.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, lastEncoding.Status);
        Assert.Equal(CliEncodingMode.Utf16, lastEncoding.LowArgs!.EncodingMode);
        Assert.Single(lastEncoding.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1252.Status);
        Assert.Equal(CliEncodingMode.Windows1252, windows1252.LowArgs!.EncodingMode);
        Assert.Single(windows1252.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88591.Status);
        Assert.Equal(CliEncodingMode.Windows1252, iso88591.LowArgs!.EncodingMode);
        Assert.Single(iso88591.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, utf8Alias.Status);
        Assert.Equal(CliEncodingMode.Utf8, utf8Alias.LowArgs!.EncodingMode);
        Assert.Single(utf8Alias.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, utf16LeAlias.Status);
        Assert.Equal(CliEncodingMode.Utf16Le, utf16LeAlias.LowArgs!.EncodingMode);
        Assert.Single(utf16LeAlias.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, utf16BeAlias.Status);
        Assert.Equal(CliEncodingMode.Utf16Be, utf16BeAlias.LowArgs!.EncodingMode);
        Assert.Single(utf16BeAlias.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, eucKr.Status);
        Assert.Equal(CliEncodingMode.EucKr, eucKr.LowArgs!.EncodingMode);
        Assert.Single(eucKr.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, eucJp.Status);
        Assert.Equal(CliEncodingMode.EucJp, eucJp.LowArgs!.EncodingMode);
        Assert.Single(eucJp.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, big5.Status);
        Assert.Equal(CliEncodingMode.Big5, big5.LowArgs!.EncodingMode);
        Assert.Single(big5.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, gb18030.Status);
        Assert.Equal(CliEncodingMode.Gb18030, gb18030.LowArgs!.EncodingMode);
        Assert.Single(gb18030.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, gbk.Status);
        Assert.Equal(CliEncodingMode.Gbk, gbk.LowArgs!.EncodingMode);
        Assert.Single(gbk.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shiftJis.Status);
        Assert.Equal(CliEncodingMode.ShiftJis, shiftJis.LowArgs!.EncodingMode);
        Assert.Single(shiftJis.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, ibm866.Status);
        Assert.Equal(CliEncodingMode.Ibm866, ibm866.LowArgs!.EncodingMode);
        Assert.Single(ibm866.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88592.Status);
        Assert.Equal(CliEncodingMode.Iso88592, iso88592.LowArgs!.EncodingMode);
        Assert.Single(iso88592.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88593.Status);
        Assert.Equal(CliEncodingMode.Iso88593, iso88593.LowArgs!.EncodingMode);
        Assert.Single(iso88593.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88594.Status);
        Assert.Equal(CliEncodingMode.Iso88594, iso88594.LowArgs!.EncodingMode);
        Assert.Single(iso88594.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88595.Status);
        Assert.Equal(CliEncodingMode.Iso88595, iso88595.LowArgs!.EncodingMode);
        Assert.Single(iso88595.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88596.Status);
        Assert.Equal(CliEncodingMode.Iso88596, iso88596.LowArgs!.EncodingMode);
        Assert.Single(iso88596.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88597.Status);
        Assert.Equal(CliEncodingMode.Iso88597, iso88597.LowArgs!.EncodingMode);
        Assert.Single(iso88597.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88598.Status);
        Assert.Equal(CliEncodingMode.Iso88598, iso88598.LowArgs!.EncodingMode);
        Assert.Single(iso88598.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso88598I.Status);
        Assert.Equal(CliEncodingMode.Iso88598I, iso88598I.LowArgs!.EncodingMode);
        Assert.Single(iso88598I.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso885910.Status);
        Assert.Equal(CliEncodingMode.Iso885910, iso885910.LowArgs!.EncodingMode);
        Assert.Single(iso885910.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso885913.Status);
        Assert.Equal(CliEncodingMode.Iso885913, iso885913.LowArgs!.EncodingMode);
        Assert.Single(iso885913.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso885914.Status);
        Assert.Equal(CliEncodingMode.Iso885914, iso885914.LowArgs!.EncodingMode);
        Assert.Single(iso885914.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso885915.Status);
        Assert.Equal(CliEncodingMode.Iso885915, iso885915.LowArgs!.EncodingMode);
        Assert.Single(iso885915.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso885916.Status);
        Assert.Equal(CliEncodingMode.Iso885916, iso885916.LowArgs!.EncodingMode);
        Assert.Single(iso885916.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, iso2022Jp.Status);
        Assert.Equal(CliEncodingMode.Iso2022Jp, iso2022Jp.LowArgs!.EncodingMode);
        Assert.Single(iso2022Jp.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, koi8r.Status);
        Assert.Equal(CliEncodingMode.Koi8R, koi8r.LowArgs!.EncodingMode);
        Assert.Single(koi8r.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, koi8u.Status);
        Assert.Equal(CliEncodingMode.Koi8U, koi8u.LowArgs!.EncodingMode);
        Assert.Single(koi8u.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, macintosh.Status);
        Assert.Equal(CliEncodingMode.Macintosh, macintosh.LowArgs!.EncodingMode);
        Assert.Single(macintosh.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows874.Status);
        Assert.Equal(CliEncodingMode.Windows874, windows874.LowArgs!.EncodingMode);
        Assert.Single(windows874.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1250.Status);
        Assert.Equal(CliEncodingMode.Windows1250, windows1250.LowArgs!.EncodingMode);
        Assert.Single(windows1250.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1251.Status);
        Assert.Equal(CliEncodingMode.Windows1251, windows1251.LowArgs!.EncodingMode);
        Assert.Single(windows1251.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1253.Status);
        Assert.Equal(CliEncodingMode.Windows1253, windows1253.LowArgs!.EncodingMode);
        Assert.Single(windows1253.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1254.Status);
        Assert.Equal(CliEncodingMode.Windows1254, windows1254.LowArgs!.EncodingMode);
        Assert.Single(windows1254.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1255.Status);
        Assert.Equal(CliEncodingMode.Windows1255, windows1255.LowArgs!.EncodingMode);
        Assert.Single(windows1255.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1256.Status);
        Assert.Equal(CliEncodingMode.Windows1256, windows1256.LowArgs!.EncodingMode);
        Assert.Single(windows1256.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1257.Status);
        Assert.Equal(CliEncodingMode.Windows1257, windows1257.LowArgs!.EncodingMode);
        Assert.Single(windows1257.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, windows1258.Status);
        Assert.Equal(CliEncodingMode.Windows1258, windows1258.LowArgs!.EncodingMode);
        Assert.Single(windows1258.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, xMacCyrillic.Status);
        Assert.Equal(CliEncodingMode.XMacCyrillic, xMacCyrillic.LowArgs!.EncodingMode);
        Assert.Single(xMacCyrillic.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, xUserDefined.Status);
        Assert.Equal(CliEncodingMode.XUserDefined, xUserDefined.LowArgs!.EncodingMode);
        Assert.Single(xUserDefined.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies encoding parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsEncodingParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--encoding"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("foo"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult upperAuto = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes("AUTO"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult trimmedNone = CliParser.Parse([OsString.FromUnixBytes("-E"u8), OsString.FromUnixBytes(" none "u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --encoding: missing argument for option '--encoding'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag -E: grep config error: unknown encoding: foo", invalid.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, upperAuto.Status);
        Assert.Equal("error parsing flag -E: grep config error: unknown encoding: AUTO", upperAuto.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, trimmedNone.Status);
        Assert.Equal("error parsing flag -E: grep config error: unknown encoding:  none ", trimmedNone.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies line-number flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [Fact]
    public void ParsesLineNumberFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("-N"u8), OsString.FromUnixBytes("--line-number"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-n"u8), OsString.FromUnixBytes("--no-line-number"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.LineNumber);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.LineNumber);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies byte-offset flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [Fact]
    public void ParsesByteOffsetFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-byte-offset"u8), OsString.FromUnixBytes("-b"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--byte-offset"u8), OsString.FromUnixBytes("--no-byte-offset"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.ByteOffset);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.ByteOffset);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies column flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesColumnFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-column"u8), OsString.FromUnixBytes("--column"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--column"u8), OsString.FromUnixBytes("--no-column"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Column);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Column);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies count mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [Fact]
    public void ParsesCountModeFlags()
    {
        CliParseResult count = CliParser.Parse(
            [OsString.FromUnixBytes("--count-matches"u8), OsString.FromUnixBytes("-c"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult countMatches = CliParser.Parse(
            [OsString.FromUnixBytes("--count"u8), OsString.FromUnixBytes("--count-matches"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, count.Status);
        Assert.Equal(CliSearchMode.Count, count.LowArgs!.SearchMode);
        Assert.Single(count.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, countMatches.Status);
        Assert.Equal(CliSearchMode.CountMatches, countMatches.LowArgs!.SearchMode);
        Assert.Single(countMatches.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies include-zero flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesIncludeZeroFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-include-zero"u8), OsString.FromUnixBytes("--include-zero"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--include-zero"u8), OsString.FromUnixBytes("--no-include-zero"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.IncludeZero);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.IncludeZero);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-count flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesMaxCountFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-m"u8), OsString.FromUnixBytes("5"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-m5"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longInline = CliParser.Parse(
            [OsString.FromUnixBytes("-m"u8), OsString.FromUnixBytes("5"u8), OsString.FromUnixBytes("--max-count=10"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortSeparate.Status);
        Assert.Equal(5UL, shortSeparate.LowArgs!.MaxCount);
        Assert.Single(shortSeparate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortInline.Status);
        Assert.Equal(5UL, shortInline.LowArgs!.MaxCount);
        Assert.Single(shortInline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longInline.Status);
        Assert.Equal(10UL, longInline.LowArgs!.MaxCount);
        Assert.Single(longInline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-count parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsMaxCountParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("-m"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--max-count=x"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag -m: missing argument for option '-m'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --max-count: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-columns flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesMaxColumnsFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-M"u8), OsString.FromUnixBytes("12"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-M12"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longInline = CliParser.Parse(
            [OsString.FromUnixBytes("-M"u8), OsString.FromUnixBytes("12"u8), OsString.FromUnixBytes("--max-columns=16"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortSeparate.Status);
        Assert.Equal(12UL, shortSeparate.LowArgs!.MaxColumns);
        Assert.Single(shortSeparate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortInline.Status);
        Assert.Equal(12UL, shortInline.LowArgs!.MaxColumns);
        Assert.Single(shortInline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longInline.Status);
        Assert.Equal(16UL, longInline.LowArgs!.MaxColumns);
        Assert.Single(longInline.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-columns preview toggles use last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesMaxColumnsPreviewFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--max-columns-preview"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--max-columns-preview"u8), OsString.FromUnixBytes("--no-max-columns-preview"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.MaxColumnsPreview);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.MaxColumnsPreview);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-columns parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsMaxColumnsParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-columns"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-Mx"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --max-columns: missing argument for option '--max-columns'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag -M: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies replacement flags accept separate, inline and empty byte values.
    /// </summary>
    [Fact]
    public void ParsesReplacementFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-r"u8), OsString.FromUnixBytes("X"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-rY"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult empty = CliParser.Parse(
            [OsString.FromUnixBytes("--replace="u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortSeparate.Status);
        Assert.Equal("X"u8.ToArray(), shortSeparate.LowArgs!.Replacement!.Value.ToArray());
        Assert.Single(shortSeparate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortInline.Status);
        Assert.Equal("Y"u8.ToArray(), shortInline.LowArgs!.Replacement!.Value.ToArray());
        Assert.Single(shortInline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, empty.Status);
        Assert.Empty(empty.LowArgs!.Replacement!.Value.ToArray());
        Assert.Single(empty.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies replacement parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsReplacementParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("-r"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag -r: missing argument for option '-r'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies color flags accept ripgrep's supported choices.
    /// </summary>
    [Fact]
    public void ParsesColorFlags()
    {
        CliParseResult always = CliParser.Parse(
            [OsString.FromUnixBytes("--color=always"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--color"u8), OsString.FromUnixBytes("never"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult ansi = CliParser.Parse(
            [OsString.FromUnixBytes("--color=ansi"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, always.Status);
        Assert.Equal(CliColorMode.Always, always.LowArgs!.ColorMode);
        Assert.Single(always.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal(CliColorMode.Never, separate.LowArgs!.ColorMode);
        Assert.Single(separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, ansi.Status);
        Assert.Equal(CliColorMode.Ansi, ansi.LowArgs!.ColorMode);
        Assert.Single(ansi.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies pretty output is parsed as ripgrep's color, heading and line-number alias.
    /// </summary>
    [Fact]
    public void ParsesPrettyFlag()
    {
        CliParseResult pretty = CliParser.Parse(
            [OsString.FromUnixBytes("-p"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult overridden = CliParser.Parse(
            [OsString.FromUnixBytes("--pretty"u8), OsString.FromUnixBytes("--color=never"u8), OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("-N"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, pretty.Status);
        Assert.Equal(CliColorMode.Always, pretty.LowArgs!.ColorMode);
        Assert.True(pretty.LowArgs.Heading);
        Assert.True(pretty.LowArgs.LineNumber);
        Assert.True(pretty.LowArgs.LineNumberSpecified);
        Assert.Single(pretty.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, overridden.Status);
        Assert.Equal(CliColorMode.Never, overridden.LowArgs!.ColorMode);
        Assert.False(overridden.LowArgs.Heading);
        Assert.False(overridden.LowArgs.LineNumber);
        Assert.True(overridden.LowArgs.LineNumberSpecified);
        Assert.Single(overridden.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrestricted flags apply ripgrep's repeated filtering levels.
    /// </summary>
    [Fact]
    public void ParsesUnrestrictedFlags()
    {
        CliParseResult one = CliParser.Parse(
            [OsString.FromUnixBytes("-u"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult two = CliParser.Parse(
            [OsString.FromUnixBytes("--unrestricted"u8), OsString.FromUnixBytes("-u"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult three = CliParser.Parse(
            [OsString.FromUnixBytes("-uuu"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, one.Status);
        Assert.Equal(1, one.LowArgs!.UnrestrictedCount);
        Assert.False(one.LowArgs.RespectIgnoreFiles);
        Assert.True(one.LowArgs.RespectExplicitIgnoreFiles);
        Assert.False(one.LowArgs.IncludeHidden);
        Assert.False(one.LowArgs.SearchBinaryFiles);
        Assert.Single(one.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, two.Status);
        Assert.Equal(2, two.LowArgs!.UnrestrictedCount);
        Assert.False(two.LowArgs.RespectIgnoreFiles);
        Assert.True(two.LowArgs.IncludeHidden);
        Assert.False(two.LowArgs.SearchBinaryFiles);
        Assert.Single(two.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, three.Status);
        Assert.Equal(3, three.LowArgs!.UnrestrictedCount);
        Assert.False(three.LowArgs.RespectIgnoreFiles);
        Assert.True(three.LowArgs.IncludeHidden);
        Assert.True(three.LowArgs.SearchBinaryFiles);
        Assert.False(three.LowArgs.TextMode);
        Assert.Single(three.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrestricted repeat-limit diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Error, shortError.Status);
        Assert.Equal("error parsing flag -u: flag can only be repeated up to 3 times", shortError.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, longError.Status);
        Assert.Equal("error parsing flag --unrestricted: flag can only be repeated up to 3 times", longError.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies color parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsColorParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--color"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--color=true"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --color: missing argument for option '--color'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --color: choice 'true' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies context flags accept separate and inline values.
    /// </summary>
    [Fact]
    public void ParsesContextFlags()
    {
        CliParseResult after = CliParser.Parse(
            [OsString.FromUnixBytes("-A=2"u8), OsString.FromUnixBytes("--after-context=3"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult before = CliParser.Parse(
            [OsString.FromUnixBytes("-B2"u8), OsString.FromUnixBytes("--before-context"u8), OsString.FromUnixBytes("3"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult context = CliParser.Parse(
            [OsString.FromUnixBytes("-C"u8), OsString.FromUnixBytes("2"u8), OsString.FromUnixBytes("--context=3"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, after.Status);
        Assert.Equal(3UL, after.LowArgs!.AfterContext);
        Assert.Equal(0UL, after.LowArgs.BeforeContext);
        Assert.Single(after.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, before.Status);
        Assert.Equal(3UL, before.LowArgs!.BeforeContext);
        Assert.Equal(0UL, before.LowArgs.AfterContext);
        Assert.Single(before.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, context.Status);
        Assert.Equal(3UL, context.LowArgs!.BeforeContext);
        Assert.Equal(3UL, context.LowArgs.AfterContext);
        Assert.Single(context.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies before and after context flags override context defaults regardless of order.
    /// </summary>
    [Fact]
    public void ParsesContextFlagPrecedence()
    {
        CliParseResult afterWins = CliParser.Parse(
            [OsString.FromUnixBytes("-A2"u8), OsString.FromUnixBytes("-C1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult beforeWins = CliParser.Parse(
            [OsString.FromUnixBytes("-C1"u8), OsString.FromUnixBytes("-B2"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, afterWins.Status);
        Assert.Equal(1UL, afterWins.LowArgs!.BeforeContext);
        Assert.Equal(2UL, afterWins.LowArgs.AfterContext);
        Assert.Single(afterWins.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, beforeWins.Status);
        Assert.Equal(2UL, beforeWins.LowArgs!.BeforeContext);
        Assert.Equal(1UL, beforeWins.LowArgs.AfterContext);
        Assert.Single(beforeWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies passthrough flags use ripgrep's order-sensitive context precedence.
    /// </summary>
    [Fact]
    public void ParsesPassthruPrecedence()
    {
        CliParseResult passthruWins = CliParser.Parse(
            [OsString.FromUnixBytes("-A1"u8), OsString.FromUnixBytes("--passthrough"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult contextWins = CliParser.Parse(
            [OsString.FromUnixBytes("--passthru"u8), OsString.FromUnixBytes("-A1"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, passthruWins.Status);
        Assert.True(passthruWins.LowArgs!.Passthru);
        Assert.Equal(1UL, passthruWins.LowArgs.AfterContext);
        Assert.Single(passthruWins.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, contextWins.Status);
        Assert.False(contextWins.LowArgs!.Passthru);
        Assert.Equal(1UL, contextWins.LowArgs.AfterContext);
        Assert.Single(contextWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies context parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsContextParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--context"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-Ax"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --context: missing argument for option '--context'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag -A: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies printer separator flags accept separate, inline, empty, and escaped byte values.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.Equal([(byte)'|'], result.LowArgs!.FieldMatchSeparator.ToArray());
        Assert.Equal([(byte)'\t'], result.LowArgs.FieldContextSeparator.ToArray());
        Assert.Equal([(byte)0x7f], result.LowArgs.ContextSeparator.ToArray());
        Assert.True(result.LowArgs.ContextSeparatorEnabled);
        Assert.Single(result.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, empty.Status);
        Assert.Empty(empty.LowArgs!.ContextSeparator.ToArray());
        Assert.True(empty.LowArgs.ContextSeparatorEnabled);
        Assert.Single(empty.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, literalInvalidEscape.Status);
        Assert.Equal([(byte)'\\', (byte)'x', (byte)'0'], literalInvalidEscape.LowArgs!.FieldMatchSeparator.ToArray());
        Assert.Single(literalInvalidEscape.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies path separator flags accept separate, inline, empty, and escaped byte values.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal((byte)'\\', separate.LowArgs!.PathSeparator);
        Assert.Single(separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal((byte)'/', inline.LowArgs!.PathSeparator);
        Assert.Single(inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, escaped.Status);
        Assert.Equal((byte)0, escaped.LowArgs!.PathSeparator);
        Assert.Single(escaped.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, empty.Status);
        Assert.Null(empty.LowArgs!.PathSeparator);
        Assert.Single(empty.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies context separator toggles use last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesContextSeparatorTogglePrecedence()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-context-separator"u8), OsString.FromUnixBytes("--context-separator=XX"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--context-separator=XX"u8), OsString.FromUnixBytes("--no-context-separator"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.ContextSeparatorEnabled);
        Assert.Equal("XX"u8.ToArray(), enabled.LowArgs.ContextSeparator.ToArray());
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.ContextSeparatorEnabled);
        Assert.Equal("XX"u8.ToArray(), disabled.LowArgs.ContextSeparator.ToArray());
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies printer separator diagnostics match ripgrep-style missing-value wording.
    /// </summary>
    [Fact]
    public void ReportsOutputSeparatorParseErrors()
    {
        CliParseResult missingField = CliParser.Parse([OsString.FromUnixBytes("--field-match-separator"u8)]);
        CliParseResult missingContext = CliParser.Parse([OsString.FromUnixBytes("--context-separator"u8)]);
        CliParseResult missingPath = CliParser.Parse([OsString.FromUnixBytes("--path-separator"u8)]);
        CliParseResult invalidPath = CliParser.Parse(
            [OsString.FromUnixBytes("--path-separator=foo"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missingField.Status);
        Assert.Equal(
            "missing value for flag --field-match-separator: missing argument for option '--field-match-separator'",
            missingField.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, missingContext.Status);
        Assert.Equal(
            "missing value for flag --context-separator: missing argument for option '--context-separator'",
            missingContext.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, missingPath.Status);
        Assert.Equal(
            "missing value for flag --path-separator: missing argument for option '--path-separator'",
            missingPath.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalidPath.Status);
        Assert.Equal(
            "error parsing flag --path-separator: A path separator must be exactly one byte, but the given separator is 3 bytes: foo\nIn some shells on Windows '/' is automatically expanded. Use '//' instead.",
            invalidPath.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-depth flags accept separate, inline and alias values with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesMaxDepthFlags()
    {
        CliParseResult shortSeparate = CliParser.Parse(
            [OsString.FromUnixBytes("-d"u8), OsString.FromUnixBytes("1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult shortInline = CliParser.Parse(
            [OsString.FromUnixBytes("-d1"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult alias = CliParser.Parse(
            [OsString.FromUnixBytes("--max-depth"u8), OsString.FromUnixBytes("1"u8), OsString.FromUnixBytes("--maxdepth=2"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortSeparate.Status);
        Assert.Equal(1UL, shortSeparate.LowArgs!.MaxDepth);
        Assert.Single(shortSeparate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortInline.Status);
        Assert.Equal(1UL, shortInline.LowArgs!.MaxDepth);
        Assert.Single(shortInline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, alias.Status);
        Assert.Equal(2UL, alias.LowArgs!.MaxDepth);
        Assert.Single(alias.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-depth parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsMaxDepthParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-depth"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("-dx"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --max-depth: missing argument for option '--max-depth'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag -d: value is not a valid number: invalid digit found in string", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies max-filesize flags accept byte values and uppercase binary suffixes.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, bytes.Status);
        Assert.Equal(4UL, bytes.LowArgs!.MaxFileSize);
        Assert.Single(bytes.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, kilobytes.Status);
        Assert.Equal(2048UL, kilobytes.LowArgs!.MaxFileSize);
        Assert.Single(kilobytes.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, megabytes.Status);
        Assert.Equal(3UL * 1024UL * 1024UL, megabytes.LowArgs!.MaxFileSize);
        Assert.Single(megabytes.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, gigabytes.Status);
        Assert.Equal(4UL * 1024UL * 1024UL * 1024UL, gigabytes.LowArgs!.MaxFileSize);
        Assert.Single(gigabytes.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies max-filesize parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsMaxFileSizeParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--max-filesize"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--max-filesize=1k"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult overflow = CliParser.Parse(
            [OsString.FromUnixBytes("--max-filesize=18446744073709551616"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --max-filesize: missing argument for option '--max-filesize'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal(
            "error parsing flag --max-filesize: invalid size: invalid format for size '1k', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix",
            invalid.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, overflow.Status);
        Assert.Equal(
            "error parsing flag --max-filesize: invalid size: invalid integer found in size '18446744073709551616': number too large to fit in target type",
            overflow.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies glob flags accept separate and inline values.
    /// </summary>
    [Fact]
    public void ParsesGlobFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("-g"u8), OsString.FromUnixBytes("*.cs"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--glob=!*.log"u8), OsString.FromUnixBytes("-g*.txt"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--glob"u8), OsString.FromUnixBytes("-foo"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal([new CliGlobPattern("*.cs", caseInsensitive: false)], separate.LowArgs!.GlobPatterns);
        Assert.Single(separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal(
            [new CliGlobPattern("!*.log", caseInsensitive: false), new CliGlobPattern("*.txt", caseInsensitive: false)],
            inline.LowArgs!.GlobPatterns);
        Assert.Single(inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, dashValue.Status);
        Assert.Equal([new CliGlobPattern("-foo", caseInsensitive: false)], dashValue.LowArgs!.GlobPatterns);
        Assert.Single(dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies case-insensitive glob flags and toggles are parsed.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, insensitive.Status);
        Assert.Equal([new CliGlobPattern("*.CS", caseInsensitive: true)], insensitive.LowArgs!.GlobPatterns);
        Assert.Single(insensitive.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal([new CliGlobPattern("*.TXT", caseInsensitive: true)], inline.LowArgs!.GlobPatterns);
        Assert.Single(inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, toggle.Status);
        Assert.False(toggle.LowArgs!.GlobCaseInsensitive);
        Assert.Equal([new CliGlobPattern("*.CS", caseInsensitive: false)], toggle.LowArgs.GlobPatterns);
        Assert.Single(toggle.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file flags accept separate and inline values.
    /// </summary>
    [Fact]
    public void ParsesIgnoreFileFlags()
    {
        CliParseResult separate = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file"u8), OsString.FromUnixBytes("first.ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult inline = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file=second.ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult dashValue = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file"u8), OsString.FromUnixBytes("-rules"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, separate.Status);
        Assert.Equal(["first.ignore"], separate.LowArgs!.IgnoreFiles);
        Assert.Single(separate.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, inline.Status);
        Assert.Equal(["second.ignore"], inline.LowArgs!.IgnoreFiles);
        Assert.Single(inline.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, dashValue.Status);
        Assert.Equal(["-rules"], dashValue.LowArgs!.IgnoreFiles);
        Assert.Single(dashValue.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file toggles use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesIgnoreFilesToggles()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file=a"u8), OsString.FromUnixBytes("--no-ignore-files"u8), OsString.FromUnixBytes("--ignore-file=b"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-ignore-files"u8), OsString.FromUnixBytes("--ignore-file=a"u8), OsString.FromUnixBytes("--ignore-files"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.RespectExplicitIgnoreFiles);
        Assert.Equal(["a", "b"], disabled.LowArgs.IgnoreFiles);
        Assert.Single(disabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.RespectExplicitIgnoreFiles);
        Assert.Equal(["a"], enabled.LowArgs.IgnoreFiles);
        Assert.Single(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies explicit ignore-file parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsIgnoreFileParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--ignore-file"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --ignore-file: missing argument for option '--ignore-file'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies iglob parser diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsInsensitiveGlobParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--iglob"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --iglob: missing argument for option '--iglob'", missing.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies sort flags accept separate and inline values with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesSortFlags()
    {
        CliParseResult ascending = CliParser.Parse(
            [OsString.FromUnixBytes("--sort"u8), OsString.FromUnixBytes("path"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult descending = CliParser.Parse(
            [OsString.FromUnixBytes("--sort=path"u8), OsString.FromUnixBytes("--sortr=modified"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sortr"u8), OsString.FromUnixBytes("created"u8), OsString.FromUnixBytes("--sort=none"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, ascending.Status);
        Assert.NotNull(ascending.LowArgs!.SortMode);
        Assert.False(ascending.LowArgs.SortMode.Value.Reverse);
        Assert.Equal(CliSortKind.Path, ascending.LowArgs.SortMode.Value.Kind);
        Assert.Single(ascending.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, descending.Status);
        Assert.NotNull(descending.LowArgs!.SortMode);
        Assert.True(descending.LowArgs.SortMode.Value.Reverse);
        Assert.Equal(CliSortKind.LastModified, descending.LowArgs.SortMode.Value.Kind);
        Assert.Single(descending.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.Null(disabled.LowArgs!.SortMode);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies deprecated sort-files flags map to path sorting and can be disabled.
    /// </summary>
    [Fact]
    public void ParsesSortFilesFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sort=created"u8), OsString.FromUnixBytes("--sort-files"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--sort-files"u8), OsString.FromUnixBytes("--no-sort-files"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.NotNull(enabled.LowArgs!.SortMode);
        Assert.False(enabled.LowArgs.SortMode.Value.Reverse);
        Assert.Equal(CliSortKind.Path, enabled.LowArgs.SortMode.Value.Kind);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.Null(disabled.LowArgs!.SortMode);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies sort parse errors use ripgrep-style wording.
    /// </summary>
    [Fact]
    public void ReportsSortParseErrors()
    {
        CliParseResult missing = CliParser.Parse([OsString.FromUnixBytes("--sort"u8)]);
        CliParseResult invalid = CliParser.Parse([OsString.FromUnixBytes("--sortr=bogus"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Error, missing.Status);
        Assert.Equal("missing value for flag --sort: missing argument for option '--sort'", missing.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, invalid.Status);
        Assert.Equal("error parsing flag --sortr: choice 'bogus' is unrecognized", invalid.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies file type selection flags accept separate and inline values.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.Equal(
            [
                new CliTypeChange(CliTypeChangeKind.Select, "cs"),
                new CliTypeChange(CliTypeChangeKind.Select, "txt"),
                new CliTypeChange(CliTypeChangeKind.Negate, "json"),
                new CliTypeChange(CliTypeChangeKind.Negate, "xml"),
            ],
            result.LowArgs!.TypeChanges);
        Assert.Single(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies file type definition flags preserve ordered changes.
    /// </summary>
    [Fact]
    public void ParsesTypeDefinitionFlags()
    {
        CliParseResult result = CliParser.Parse(
            [
                OsString.FromUnixBytes("--type-clear=foo"u8),
                OsString.FromUnixBytes("--type-add"u8),
                OsString.FromUnixBytes("foo:*.foo"u8),
                OsString.FromUnixBytes("--type-list"u8),
            ]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.True(result.LowArgs!.TypeList);
        Assert.Equal(
            [
                new CliTypeChange(CliTypeChangeKind.Clear, "foo"),
                new CliTypeChange(CliTypeChangeKind.Add, "foo:*.foo"),
            ],
            result.LowArgs.TypeChanges);
        Assert.Empty(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies type parser missing-value diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ReportsTypeParseErrors()
    {
        CliParseResult missingType = CliParser.Parse([OsString.FromUnixBytes("-t"u8)]);
        CliParseResult missingTypeAdd = CliParser.Parse([OsString.FromUnixBytes("--type-add"u8)]);

        Assert.Equal(CliParseStatus.Error, missingType.Status);
        Assert.Equal("missing value for flag -t: missing argument for option '-t'", missingType.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, missingTypeAdd.Status);
        Assert.Equal("missing value for flag --type-add: missing argument for option '--type-add'", missingTypeAdd.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies trim flags use last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesTrimFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-trim"u8), OsString.FromUnixBytes("--trim"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--trim"u8), OsString.FromUnixBytes("--no-trim"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Trim);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Trim);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies heading flags use last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesHeadingFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("--heading"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--heading"u8), OsString.FromUnixBytes("--no-heading"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Heading);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Heading);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies files mode treats positional arguments as paths.
    /// </summary>
    [Fact]
    public void ParsesFilesMode()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--files"u8), OsString.FromUnixBytes("src"u8)]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.Equal(CliSearchMode.Files, result.LowArgs!.SearchMode);
        Assert.Single(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies file-list mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, withMatches.Status);
        Assert.Equal(CliSearchMode.FilesWithMatches, withMatches.LowArgs!.SearchMode);
        Assert.Single(withMatches.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, withoutMatch.Status);
        Assert.Equal(CliSearchMode.FilesWithoutMatch, withoutMatch.LowArgs!.SearchMode);
        Assert.Single(withoutMatch.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, filesThenWithMatches.Status);
        Assert.Equal(CliSearchMode.FilesWithMatches, filesThenWithMatches.LowArgs!.SearchMode);
        Assert.Single(filesThenWithMatches.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, withMatchesThenFiles.Status);
        Assert.Equal(CliSearchMode.Files, withMatchesThenFiles.LowArgs!.SearchMode);
        Assert.Single(withMatchesThenFiles.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies JSON mode flags follow ripgrep mode precedence.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, json.Status);
        Assert.Equal(CliSearchMode.Json, json.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.Equal(CliSearchMode.Standard, disabled.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, countThenJson.Status);
        Assert.Equal(CliSearchMode.Json, countThenJson.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, jsonThenCount.Status);
        Assert.Equal(CliSearchMode.Count, jsonThenCount.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, filesThenJson.Status);
        Assert.Equal(CliSearchMode.Json, filesThenJson.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, jsonThenFiles.Status);
        Assert.Equal(CliSearchMode.Files, jsonThenFiles.LowArgs!.SearchMode);
    }

    /// <summary>
    /// Verifies fixed-string mode flags are parsed.
    /// </summary>
    [Fact]
    public void ParsesFixedStringsFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-F"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--fixed-strings"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.FixedStrings);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.FixedStrings);
        Assert.Single(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies quiet mode flags are parsed.
    /// </summary>
    [Fact]
    public void ParsesQuietFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-q"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--quiet"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.Quiet);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.Quiet);
        Assert.Single(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies stats flags use last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesStatsFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-stats"u8), OsString.FromUnixBytes("--stats"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--stats"u8), OsString.FromUnixBytes("--no-stats"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.Stats);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.Stats);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies only-matching flags are parsed.
    /// </summary>
    [Fact]
    public void ParsesOnlyMatchingFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-o"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--only-matching"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.OnlyMatching);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.OnlyMatching);
        Assert.Single(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies vimgrep output mode is parsed.
    /// </summary>
    [Fact]
    public void ParsesVimgrepFlag()
    {
        CliParseResult result = CliParser.Parse(
            [OsString.FromUnixBytes("--vimgrep"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.True(result.LowArgs!.Vimgrep);
        Assert.Single(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies null path terminator flags are parsed.
    /// </summary>
    [Fact]
    public void ParsesNullPathTerminatorFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-0"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--null"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.NullPathTerminator);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.NullPathTerminator);
        Assert.Single(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies invert-match flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesInvertMatchFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--invert-match"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-v"u8), OsString.FromUnixBytes("--no-invert-match"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.InvertMatch);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.InvertMatch);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies line-regexp flags are parsed.
    /// </summary>
    [Fact]
    public void ParsesLineRegexpFlags()
    {
        CliParseResult shortFlag = CliParser.Parse(
            [OsString.FromUnixBytes("-x"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult longFlag = CliParser.Parse(
            [OsString.FromUnixBytes("--line-regexp"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.LineRegexp);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.LineRegexp);
        Assert.Single(longFlag.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies word-regexp flags are parsed and use last-wins behavior with line-regexp.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, shortFlag.Status);
        Assert.True(shortFlag.LowArgs!.WordRegexp);
        Assert.False(shortFlag.LowArgs.LineRegexp);
        Assert.Single(shortFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longFlag.Status);
        Assert.True(longFlag.LowArgs!.WordRegexp);
        Assert.False(longFlag.LowArgs.LineRegexp);
        Assert.Single(longFlag.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, wordWins.Status);
        Assert.True(wordWins.LowArgs!.WordRegexp);
        Assert.False(wordWins.LowArgs.LineRegexp);
        Assert.Single(wordWins.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, lineWins.Status);
        Assert.False(lineWins.LowArgs!.WordRegexp);
        Assert.True(lineWins.LowArgs.LineRegexp);
        Assert.Single(lineWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies filename-prefix flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, shortWith.Status);
        Assert.True(shortWith.LowArgs!.WithFilename);
        Assert.Single(shortWith.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, longWith.Status);
        Assert.True(longWith.LowArgs!.WithFilename);
        Assert.Single(longWith.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, shortWithout.Status);
        Assert.False(shortWithout.LowArgs!.WithFilename);
        Assert.Single(shortWithout.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, withoutWins.Status);
        Assert.False(withoutWins.LowArgs!.WithFilename);
        Assert.Single(withoutWins.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, withWins.Status);
        Assert.True(withWins.LowArgs!.WithFilename);
        Assert.Single(withWins.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies case mode flags are parsed with the same last-wins behavior as ripgrep.
    /// </summary>
    [Fact]
    public void ParsesCaseModeFlags()
    {
        CliParseResult sensitive = CliParser.Parse(
            [OsString.FromUnixBytes("-i"u8), OsString.FromUnixBytes("-s"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult insensitive = CliParser.Parse(
            [OsString.FromUnixBytes("-s"u8), OsString.FromUnixBytes("--ignore-case"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult smart = CliParser.Parse(
            [OsString.FromUnixBytes("--smart-case"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, sensitive.Status);
        Assert.Equal(CliCaseMode.Sensitive, sensitive.LowArgs!.CaseMode);
        Assert.Single(sensitive.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, insensitive.Status);
        Assert.Equal(CliCaseMode.Insensitive, insensitive.LowArgs!.CaseMode);
        Assert.Single(insensitive.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, smart.Status);
        Assert.Equal(CliCaseMode.Smart, smart.LowArgs!.CaseMode);
        Assert.Single(smart.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies hidden traversal flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesHiddenFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-hidden"u8), OsString.FromUnixBytes("--hidden"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("-."u8), OsString.FromUnixBytes("--no-hidden"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.IncludeHidden);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.IncludeHidden);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies follow flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesFollowFlags()
    {
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-follow"u8), OsString.FromUnixBytes("-L"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--follow"u8), OsString.FromUnixBytes("--no-follow"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.FollowLinks);
        Assert.Single(enabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.FollowLinks);
        Assert.Single(disabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies ignore-file flags are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesIgnoreFlags()
    {
        CliParseResult disabled = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore"u8), OsString.FromUnixBytes("--no-ignore"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult enabled = CliParser.Parse(
            [OsString.FromUnixBytes("--no-ignore"u8), OsString.FromUnixBytes("--ignore"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, disabled.Status);
        Assert.False(disabled.LowArgs!.RespectIgnoreFiles);
        Assert.Single(disabled.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, enabled.Status);
        Assert.True(enabled.LowArgs!.RespectIgnoreFiles);
        Assert.Single(enabled.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies ignore source flags are parsed independently with last-wins behavior.
    /// </summary>
    [Fact]
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

        Assert.Equal(CliParseStatus.Ok, result.Status);
        Assert.False(result.LowArgs!.RespectIgnoreFiles);
        Assert.True(result.LowArgs.RespectDotIgnoreFiles);
        Assert.True(result.LowArgs.RespectGitIgnoreFiles);
        Assert.False(result.LowArgs.RespectGitExcludeFiles);
        Assert.False(result.LowArgs.RespectGlobalIgnoreFiles);
        Assert.True(result.LowArgs.RespectParentIgnoreFiles);
        Assert.False(result.LowArgs.IgnoreMessages);
        Assert.Single(result.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies traversal ignore modifiers are parsed with last-wins behavior.
    /// </summary>
    [Fact]
    public void ParsesTraversalIgnoreModifiers()
    {
        CliParseResult requireGit = CliParser.Parse(
            [OsString.FromUnixBytes("--no-require-git"u8), OsString.FromUnixBytes("--require-git"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult ignoreCase = CliParser.Parse(
            [OsString.FromUnixBytes("--ignore-file-case-insensitive"u8), OsString.FromUnixBytes("--no-ignore-file-case-insensitive"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult fileSystem = CliParser.Parse(
            [OsString.FromUnixBytes("--no-one-file-system"u8), OsString.FromUnixBytes("--one-file-system"u8), OsString.FromUnixBytes("needle"u8)]);

        Assert.Equal(CliParseStatus.Ok, requireGit.Status);
        Assert.True(requireGit.LowArgs!.RequireGitRepository);
        Assert.Single(requireGit.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, ignoreCase.Status);
        Assert.False(ignoreCase.LowArgs!.IgnoreFileCaseInsensitive);
        Assert.Single(ignoreCase.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, fileSystem.Status);
        Assert.True(fileSystem.LowArgs!.OneFileSystem);
        Assert.Single(fileSystem.LowArgs.Positional);
    }

    /// <summary>
    /// Verifies unrecognized long flags match ripgrep's low-level message.
    /// </summary>
    [Fact]
    public void ReportsUnrecognizedLongFlag()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes("--bogus"u8)]);

        Assert.Equal(CliParseStatus.Error, result.Status);
        Assert.Equal("unrecognized flag --bogus", result.Error!.FormatAlternate());
    }

    /// <summary>
    /// Verifies invalid UTF-8 in flag names is rejected before text replacement can occur.
    /// </summary>
    [Fact]
    public void RejectsInvalidUtf8FlagName()
    {
        CliParseResult result = CliParser.Parse([OsString.FromUnixBytes([0x2d, 0x2d, 0xff])]);

        Assert.Equal(CliParseStatus.Error, result.Status);
        Assert.Equal("invalid CLI arguments", result.Error!.FormatAlternate());
    }
}
