using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Scout;

/// <summary>
/// Verifies initial application dispatch behavior.
/// </summary>
public sealed class ScoutApplicationTests
{
    /// <summary>
    /// Verifies the short version mode writes the pinned upstream version bytes.
    /// </summary>
    [Fact]
    public void ShortVersionWritesPinnedUpstreamVersion()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-V"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("ripgrep 15.1.0 (rev 4857d6fa67)\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies the long version mode writes the pinned upstream long version bytes.
    /// </summary>
    [Fact]
    public void LongVersionWritesPinnedUpstreamVersion()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--version"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "ripgrep 15.1.0 (rev 4857d6fa67)\n\nfeatures:-pcre2\nsimd(compile):+NEON\nsimd(runtime):+NEON\n\nPCRE2 is not available in this build of ripgrep.\n\n"u8.ToArray(),
            output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies short help output matches the pinned upstream binary.
    /// </summary>
    [Fact]
    public void ShortHelpWritesPinnedUpstreamHelp()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-h"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal(ReadPinnedRipgrepOutput("-h"), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies long help output matches the pinned upstream binary.
    /// </summary>
    [Fact]
    public void LongHelpWritesPinnedUpstreamHelp()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--help"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal(ReadPinnedRipgrepOutput("--help"), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies parser errors are rendered with ripgrep's top-level prefix.
    /// </summary>
    [Fact]
    public void ParserErrorsUseRipgrepPrefix()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--bogus"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Equal("rg: unrecognized flag --bogus\n"u8.ToArray(), error.ToArray());
    }

    /// <summary>
    /// Verifies missing patterns match ripgrep's diagnostic.
    /// </summary>
    [Fact]
    public void MissingPatternWritesRipgrepDiagnosticError()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Equal("rg: ripgrep requires at least one pattern to execute a search\n"u8.ToArray(), error.ToArray());
    }

    /// <summary>
    /// Verifies a literal search prints matching lines from a single file without path prefixes.
    /// </summary>
    [Fact]
    public void LiteralSearchPrintsMatchingLinesForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nbeta needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("needle one\nbeta needle two\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies line-number output for a single searched file.
    /// </summary>
    [Fact]
    public void LiteralSearchPrintsLineNumbersForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\nbeta needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("2:needle one\n3:beta needle two\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies bundled short flags match pinned ripgrep behavior.
    /// </summary>
    [Fact]
    public void CombinedShortFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nmiss\nneedle again\n");

        (int switchesExitCode, byte[] switchesOutput, string switchesError) = RunScout("-nH", "needle", path);
        (int pinnedSwitchesExitCode, byte[] pinnedSwitchesOutput, string pinnedSwitchesError) = RunPinnedRipgrep("-nH", "needle", path);
        (int valueExitCode, byte[] valueOutput, string valueError) = RunScout("-nA1", "needle", path);
        (int pinnedValueExitCode, byte[] pinnedValueOutput, string pinnedValueError) = RunPinnedRipgrep("-nA1", "needle", path);
        (int followingExitCode, byte[] followingOutput, string followingError) = RunScout("-ne", "needle", path);
        (int pinnedFollowingExitCode, byte[] pinnedFollowingOutput, string pinnedFollowingError) = RunPinnedRipgrep("-ne", "needle", path);
        (int invalidExitCode, byte[] invalidOutput, string invalidError) = RunScout("-m1n", "needle", path);
        (int pinnedInvalidExitCode, byte[] pinnedInvalidOutput, string pinnedInvalidError) = RunPinnedRipgrep("-m1n", "needle", path);

        Assert.Equal(pinnedSwitchesExitCode, switchesExitCode);
        Assert.Equal(pinnedSwitchesOutput, switchesOutput);
        Assert.Equal(pinnedSwitchesError, switchesError);
        Assert.Equal(pinnedValueExitCode, valueExitCode);
        Assert.Equal(pinnedValueOutput, valueOutput);
        Assert.Equal(pinnedValueError, valueError);
        Assert.Equal(pinnedFollowingExitCode, followingExitCode);
        Assert.Equal(pinnedFollowingOutput, followingOutput);
        Assert.Equal(pinnedFollowingError, followingError);
        Assert.Equal(pinnedInvalidExitCode, invalidExitCode);
        Assert.Equal(pinnedInvalidOutput, invalidOutput);
        Assert.Equal(pinnedInvalidError, invalidError);
    }

    /// <summary>
    /// Verifies the no-line-number flag disables earlier line-number output.
    /// </summary>
    [Fact]
    public void LiteralSearchNoLineNumberFlagDisablesEarlierLineNumberFlag()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle one\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("-N"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("needle one\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies fixed-string mode treats regex metacharacters literally.
    /// </summary>
    [Fact]
    public void FixedStringsTreatsRegexMetacharactersLiterally()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "a.c\nabc\na-c\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-F"u8),
            OsString.FromUnixBytes("a.c"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-F", "a.c", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies default regex mode treats dot as a one-byte wildcard.
    /// </summary>
    [Fact]
    public void DefaultRegexDotMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "a.c\nabc\na-c\nac\n");

        (int exitCode, byte[] output, string error) = RunScout("a.c", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("a.c", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default regex mode supports byte character classes and escaped metacharacters.
    /// </summary>
    [Fact]
    public void DefaultRegexClassMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "file7.txt\nfilex.txt\nfile12.txt\n");

        (int exitCode, byte[] output, string error) = RunScout(@"file[0-9]\.txt", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(@"file[0-9]\.txt", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default regex mode supports POSIX and shorthand bracket classes.
    /// </summary>
    [Fact]
    public void DefaultRegexNestedClassSyntaxMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc 123\n_Z-9\nspace\ttab\n");

        (int digitExitCode, byte[] digitOutput, string digitError) = RunScout("-o", "[[:digit:]]+", path);
        (int pinnedDigitExitCode, byte[] pinnedDigitOutput, string pinnedDigitError) = RunPinnedRipgrep("-o", "[[:digit:]]+", path);
        (int alphaExitCode, byte[] alphaOutput, string alphaError) = RunScout("-o", "[[:alpha:]]+", path);
        (int pinnedAlphaExitCode, byte[] pinnedAlphaOutput, string pinnedAlphaError) = RunPinnedRipgrep("-o", "[[:alpha:]]+", path);
        (int escapedDigitExitCode, byte[] escapedDigitOutput, string escapedDigitError) = RunScout("-o", @"[\d]+", path);
        (int pinnedEscapedDigitExitCode, byte[] pinnedEscapedDigitOutput, string pinnedEscapedDigitError) = RunPinnedRipgrep("-o", @"[\d]+", path);
        (int wordExitCode, byte[] wordOutput, string wordError) = RunScout("-o", @"[\w]+", path);
        (int pinnedWordExitCode, byte[] pinnedWordOutput, string pinnedWordError) = RunPinnedRipgrep("-o", @"[\w]+", path);
        (int spaceExitCode, byte[] spaceOutput, string spaceError) = RunScout("-o", @"[\s]+", path);
        (int pinnedSpaceExitCode, byte[] pinnedSpaceOutput, string pinnedSpaceError) = RunPinnedRipgrep("-o", @"[\s]+", path);
        (int negatedExitCode, byte[] negatedOutput, string negatedError) = RunScout("-o", "[[:^digit:]]+", path);
        (int pinnedNegatedExitCode, byte[] pinnedNegatedOutput, string pinnedNegatedError) = RunPinnedRipgrep("-o", "[[:^digit:]]+", path);

        Assert.Equal(pinnedDigitExitCode, digitExitCode);
        Assert.Equal(pinnedDigitOutput, digitOutput);
        Assert.Equal(pinnedDigitError, digitError);
        Assert.Equal(pinnedAlphaExitCode, alphaExitCode);
        Assert.Equal(pinnedAlphaOutput, alphaOutput);
        Assert.Equal(pinnedAlphaError, alphaError);
        Assert.Equal(pinnedEscapedDigitExitCode, escapedDigitExitCode);
        Assert.Equal(pinnedEscapedDigitOutput, escapedDigitOutput);
        Assert.Equal(pinnedEscapedDigitError, escapedDigitError);
        Assert.Equal(pinnedWordExitCode, wordExitCode);
        Assert.Equal(pinnedWordOutput, wordOutput);
        Assert.Equal(pinnedWordError, wordError);
        Assert.Equal(pinnedSpaceExitCode, spaceExitCode);
        Assert.Equal(pinnedSpaceOutput, spaceOutput);
        Assert.Equal(pinnedSpaceError, spaceError);
        Assert.Equal(pinnedNegatedExitCode, negatedExitCode);
        Assert.Equal(pinnedNegatedOutput, negatedOutput);
        Assert.Equal(pinnedNegatedError, negatedError);
    }

    /// <summary>
    /// Verifies default regex mode supports greedy one-byte repetition.
    /// </summary>
    [Fact]
    public void DefaultRegexQuantifiersMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "ac\nabc\nabbc\nabbbc\nabdc\n");

        (int exitCode, byte[] output, string error) = RunScout("-o", "ab+c", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", "ab+c", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default regex mode supports counted repetition ranges.
    /// </summary>
    [Fact]
    public void DefaultRegexCountedRepetitionMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "ab\nabb\nabbb\nabbbb\nabab\nababab\nfoo12\nfoo123\nfoo1234\n");

        (int literalExitCode, byte[] literalOutput, string literalError) = RunScout("-n", "ab{2,3}", path);
        (int pinnedLiteralExitCode, byte[] pinnedLiteralOutput, string pinnedLiteralError) = RunPinnedRipgrep("-n", "ab{2,3}", path);
        (int groupExitCode, byte[] groupOutput, string groupError) = RunScout("-n", "^(ab){2,3}$", path);
        (int pinnedGroupExitCode, byte[] pinnedGroupOutput, string pinnedGroupError) = RunPinnedRipgrep("-n", "^(ab){2,3}$", path);
        (int classExitCode, byte[] classOutput, string classError) = RunScout("-o", "foo[0-9]{2,3}", path);
        (int pinnedClassExitCode, byte[] pinnedClassOutput, string pinnedClassError) = RunPinnedRipgrep("-o", "foo[0-9]{2,3}", path);
        (int unboundedExitCode, byte[] unboundedOutput, string unboundedError) = RunScout("-n", "ab{2,}", path);
        (int pinnedUnboundedExitCode, byte[] pinnedUnboundedOutput, string pinnedUnboundedError) = RunPinnedRipgrep("-n", "ab{2,}", path);

        Assert.Equal(pinnedLiteralExitCode, literalExitCode);
        Assert.Equal(pinnedLiteralOutput, literalOutput);
        Assert.Equal(pinnedLiteralError, literalError);
        Assert.Equal(pinnedGroupExitCode, groupExitCode);
        Assert.Equal(pinnedGroupOutput, groupOutput);
        Assert.Equal(pinnedGroupError, groupError);
        Assert.Equal(pinnedClassExitCode, classExitCode);
        Assert.Equal(pinnedClassOutput, classOutput);
        Assert.Equal(pinnedClassError, classError);
        Assert.Equal(pinnedUnboundedExitCode, unboundedExitCode);
        Assert.Equal(pinnedUnboundedOutput, unboundedOutput);
        Assert.Equal(pinnedUnboundedError, unboundedError);
    }

    /// <summary>
    /// Verifies default regex mode supports lazy repetition spans.
    /// </summary>
    [Fact]
    public void DefaultRegexLazyQuantifiersMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abbb\na123b456b\nababab\n");

        (int oneByteExitCode, byte[] oneByteOutput, string oneByteError) = RunScout("-o", "ab+?", path);
        (int pinnedOneByteExitCode, byte[] pinnedOneByteOutput, string pinnedOneByteError) = RunPinnedRipgrep("-o", "ab+?", path);
        (int dotExitCode, byte[] dotOutput, string dotError) = RunScout("-o", "a.*?b", path);
        (int pinnedDotExitCode, byte[] pinnedDotOutput, string pinnedDotError) = RunPinnedRipgrep("-o", "a.*?b", path);
        (int groupExitCode, byte[] groupOutput, string groupError) = RunScout("-o", "(ab)+?", path);
        (int pinnedGroupExitCode, byte[] pinnedGroupOutput, string pinnedGroupError) = RunPinnedRipgrep("-o", "(ab)+?", path);
        (int countedExitCode, byte[] countedOutput, string countedError) = RunScout("-o", "ab{1,3}?", path);
        (int pinnedCountedExitCode, byte[] pinnedCountedOutput, string pinnedCountedError) = RunPinnedRipgrep("-o", "ab{1,3}?", path);

        Assert.Equal(pinnedOneByteExitCode, oneByteExitCode);
        Assert.Equal(pinnedOneByteOutput, oneByteOutput);
        Assert.Equal(pinnedOneByteError, oneByteError);
        Assert.Equal(pinnedDotExitCode, dotExitCode);
        Assert.Equal(pinnedDotOutput, dotOutput);
        Assert.Equal(pinnedDotError, dotError);
        Assert.Equal(pinnedGroupExitCode, groupExitCode);
        Assert.Equal(pinnedGroupOutput, groupOutput);
        Assert.Equal(pinnedGroupError, groupError);
        Assert.Equal(pinnedCountedExitCode, countedExitCode);
        Assert.Equal(pinnedCountedOutput, countedOutput);
        Assert.Equal(pinnedCountedError, countedError);
    }

    /// <summary>
    /// Verifies default regex mode supports ungreedy inline flags.
    /// </summary>
    [Fact]
    public void DefaultRegexUngreedyFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "a123b456b\nabbbbc\nab\n");

        (int enabledExitCode, byte[] enabledOutput, string enabledError) = RunScout("-o", "(?U)a.*b", path);
        (int pinnedEnabledExitCode, byte[] pinnedEnabledOutput, string pinnedEnabledError) = RunPinnedRipgrep("-o", "(?U)a.*b", path);
        (int lazySuffixExitCode, byte[] lazySuffixOutput, string lazySuffixError) = RunScout("-o", "(?U)a.*?b", path);
        (int pinnedLazySuffixExitCode, byte[] pinnedLazySuffixOutput, string pinnedLazySuffixError) = RunPinnedRipgrep("-o", "(?U)a.*?b", path);
        (int plusExitCode, byte[] plusOutput, string plusError) = RunScout("-o", "(?U)ab+?c", path);
        (int pinnedPlusExitCode, byte[] pinnedPlusOutput, string pinnedPlusError) = RunPinnedRipgrep("-o", "(?U)ab+?c", path);
        (int scopedExitCode, byte[] scopedOutput, string scopedError) = RunScout("-o", "(?U:ab+)", path);
        (int pinnedScopedExitCode, byte[] pinnedScopedOutput, string pinnedScopedError) = RunPinnedRipgrep("-o", "(?U:ab+)", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-o", "(?U)(?-U:ab+)", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-o", "(?U)(?-U:ab+)", path);

        Assert.Equal(pinnedEnabledExitCode, enabledExitCode);
        Assert.Equal(pinnedEnabledOutput, enabledOutput);
        Assert.Equal(pinnedEnabledError, enabledError);
        Assert.Equal(pinnedLazySuffixExitCode, lazySuffixExitCode);
        Assert.Equal(pinnedLazySuffixOutput, lazySuffixOutput);
        Assert.Equal(pinnedLazySuffixError, lazySuffixError);
        Assert.Equal(pinnedPlusExitCode, plusExitCode);
        Assert.Equal(pinnedPlusOutput, plusOutput);
        Assert.Equal(pinnedPlusError, plusError);
        Assert.Equal(pinnedScopedExitCode, scopedExitCode);
        Assert.Equal(pinnedScopedOutput, scopedOutput);
        Assert.Equal(pinnedScopedError, scopedError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
    }

    /// <summary>
    /// Verifies default regex mode supports inline case-insensitive flags.
    /// </summary>
    [Fact]
    public void DefaultRegexInlineCaseFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "FOO\nfoo\nFooBar\nfooBAR\nfoobar\nbarFOO\n");

        (int enabledExitCode, byte[] enabledOutput, string enabledError) = RunScout("-n", "(?i)foo", path);
        (int pinnedEnabledExitCode, byte[] pinnedEnabledOutput, string pinnedEnabledError) = RunPinnedRipgrep("-n", "(?i)foo", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-n", "(?i)foo(?-i)bar", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-n", "(?i)foo(?-i)bar", path);
        (int forcedSensitiveExitCode, byte[] forcedSensitiveOutput, string forcedSensitiveError) = RunScout("-i", "-n", "(?-i)foo", path);
        (int pinnedForcedSensitiveExitCode, byte[] pinnedForcedSensitiveOutput, string pinnedForcedSensitiveError) = RunPinnedRipgrep("-i", "-n", "(?-i)foo", path);

        Assert.Equal(pinnedEnabledExitCode, enabledExitCode);
        Assert.Equal(pinnedEnabledOutput, enabledOutput);
        Assert.Equal(pinnedEnabledError, enabledError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
        Assert.Equal(pinnedForcedSensitiveExitCode, forcedSensitiveExitCode);
        Assert.Equal(pinnedForcedSensitiveOutput, forcedSensitiveOutput);
        Assert.Equal(pinnedForcedSensitiveError, forcedSensitiveError);
    }

    /// <summary>
    /// Verifies default regex mode supports inline ignore-whitespace flags.
    /// </summary>
    [Fact]
    public void DefaultRegexInlineIgnoreWhitespaceFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfo o\nfoobar\nfoo b a r\nfo#o\nfoo#bar\nbar\nb a r\n");

        (int enabledExitCode, byte[] enabledOutput, string enabledError) = RunScout("-n", "(?x) f o o", path);
        (int pinnedEnabledExitCode, byte[] pinnedEnabledOutput, string pinnedEnabledError) = RunPinnedRipgrep("-n", "(?x) f o o", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-n", "(?x) f o o (?-x)bar", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-n", "(?x) f o o (?-x)bar", path);
        (int escapedSpaceExitCode, byte[] escapedSpaceOutput, string escapedSpaceError) = RunScout("-n", @"(?x)fo\ o", path);
        (int pinnedEscapedSpaceExitCode, byte[] pinnedEscapedSpaceOutput, string pinnedEscapedSpaceError) = RunPinnedRipgrep("-n", @"(?x)fo\ o", path);
        (int scopedExitCode, byte[] scopedOutput, string scopedError) = RunScout("-n", "(?x: f o o)(?-x:bar)", path);
        (int pinnedScopedExitCode, byte[] pinnedScopedOutput, string pinnedScopedError) = RunPinnedRipgrep("-n", "(?x: f o o)(?-x:bar)", path);
        (int significantExitCode, byte[] significantOutput, string significantError) = RunScout("-n", "(?x) f o o (?-x) b a r", path);
        (int pinnedSignificantExitCode, byte[] pinnedSignificantOutput, string pinnedSignificantError) = RunPinnedRipgrep("-n", "(?x) f o o (?-x) b a r", path);
        (int commentExitCode, byte[] commentOutput, string commentError) = RunScout("-n", "(?x) f # comment\n o o", path);
        (int pinnedCommentExitCode, byte[] pinnedCommentOutput, string pinnedCommentError) = RunPinnedRipgrep("-n", "(?x) f # comment\n o o", path);
        (int escapedHashExitCode, byte[] escapedHashOutput, string escapedHashError) = RunScout("-n", @"(?x)fo\#o", path);
        (int pinnedEscapedHashExitCode, byte[] pinnedEscapedHashOutput, string pinnedEscapedHashError) = RunPinnedRipgrep("-n", @"(?x)fo\#o", path);
        (int disabledHashExitCode, byte[] disabledHashOutput, string disabledHashError) = RunScout("-n", "(?x)foo(?-x)#bar", path);
        (int pinnedDisabledHashExitCode, byte[] pinnedDisabledHashOutput, string pinnedDisabledHashError) = RunPinnedRipgrep("-n", "(?x)foo(?-x)#bar", path);
        (int commentAlternationExitCode, byte[] commentAlternationOutput, string commentAlternationError) = RunScout("-n", "(?x)foo # | ignored\nbar", path);
        (int pinnedCommentAlternationExitCode, byte[] pinnedCommentAlternationOutput, string pinnedCommentAlternationError) = RunPinnedRipgrep("-n", "(?x)foo # | ignored\nbar", path);
        (int carriedFlagExitCode, byte[] carriedFlagOutput, string carriedFlagError) = RunScout("-n", "(?x)nomatch|b a r", path);
        (int pinnedCarriedFlagExitCode, byte[] pinnedCarriedFlagOutput, string pinnedCarriedFlagError) = RunPinnedRipgrep("-n", "(?x)nomatch|b a r", path);

        Assert.Equal(pinnedEnabledExitCode, enabledExitCode);
        Assert.Equal(pinnedEnabledOutput, enabledOutput);
        Assert.Equal(pinnedEnabledError, enabledError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
        Assert.Equal(pinnedEscapedSpaceExitCode, escapedSpaceExitCode);
        Assert.Equal(pinnedEscapedSpaceOutput, escapedSpaceOutput);
        Assert.Equal(pinnedEscapedSpaceError, escapedSpaceError);
        Assert.Equal(pinnedScopedExitCode, scopedExitCode);
        Assert.Equal(pinnedScopedOutput, scopedOutput);
        Assert.Equal(pinnedScopedError, scopedError);
        Assert.Equal(pinnedSignificantExitCode, significantExitCode);
        Assert.Equal(pinnedSignificantOutput, significantOutput);
        Assert.Equal(pinnedSignificantError, significantError);
        Assert.Equal(pinnedCommentExitCode, commentExitCode);
        Assert.Equal(pinnedCommentOutput, commentOutput);
        Assert.Equal(pinnedCommentError, commentError);
        Assert.Equal(pinnedEscapedHashExitCode, escapedHashExitCode);
        Assert.Equal(pinnedEscapedHashOutput, escapedHashOutput);
        Assert.Equal(pinnedEscapedHashError, escapedHashError);
        Assert.Equal(pinnedDisabledHashExitCode, disabledHashExitCode);
        Assert.Equal(pinnedDisabledHashOutput, disabledHashOutput);
        Assert.Equal(pinnedDisabledHashError, disabledHashError);
        Assert.Equal(pinnedCommentAlternationExitCode, commentAlternationExitCode);
        Assert.Equal(pinnedCommentAlternationOutput, commentAlternationOutput);
        Assert.Equal(pinnedCommentAlternationError, commentAlternationError);
        Assert.Equal(pinnedCarriedFlagExitCode, carriedFlagExitCode);
        Assert.Equal(pinnedCarriedFlagOutput, carriedFlagOutput);
        Assert.Equal(pinnedCarriedFlagError, carriedFlagError);
    }

    /// <summary>
    /// Verifies default regex mode supports scoped inline case-insensitive groups.
    /// </summary>
    [Fact]
    public void DefaultRegexScopedInlineCaseFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "FOObar\nFooBar\nfoobar\nfooBAR\nFOOBAR\n");

        (int scopedExitCode, byte[] scopedOutput, string scopedError) = RunScout("-n", "(?i:foo)bar", path);
        (int pinnedScopedExitCode, byte[] pinnedScopedOutput, string pinnedScopedError) = RunPinnedRipgrep("-n", "(?i:foo)bar", path);
        (int restoredExitCode, byte[] restoredOutput, string restoredError) = RunScout("-n", "(?i:foo)(?-i:bar)", path);
        (int pinnedRestoredExitCode, byte[] pinnedRestoredOutput, string pinnedRestoredError) = RunPinnedRipgrep("-n", "(?i:foo)(?-i:bar)", path);
        (int repeatedExitCode, byte[] repeatedOutput, string repeatedError) = RunScout("-n", "(?i:(foo){1,2})bar", path);
        (int pinnedRepeatedExitCode, byte[] pinnedRepeatedOutput, string pinnedRepeatedError) = RunPinnedRipgrep("-n", "(?i:(foo){1,2})bar", path);

        Assert.Equal(pinnedScopedExitCode, scopedExitCode);
        Assert.Equal(pinnedScopedOutput, scopedOutput);
        Assert.Equal(pinnedScopedError, scopedError);
        Assert.Equal(pinnedRestoredExitCode, restoredExitCode);
        Assert.Equal(pinnedRestoredOutput, restoredOutput);
        Assert.Equal(pinnedRestoredError, restoredError);
        Assert.Equal(pinnedRepeatedExitCode, repeatedExitCode);
        Assert.Equal(pinnedRepeatedOutput, repeatedOutput);
        Assert.Equal(pinnedRepeatedError, repeatedError);
    }

    /// <summary>
    /// Verifies default regex mode supports line anchors.
    /// </summary>
    [Fact]
    public void DefaultRegexAnchorsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle start\nxx needle\nneedle\nend needle\n");

        (int startExitCode, byte[] startOutput, string startError) = RunScout("-n", "^needle", path);
        (int pinnedStartExitCode, byte[] pinnedStartOutput, string pinnedStartError) = RunPinnedRipgrep("-n", "^needle", path);
        (int endExitCode, byte[] endOutput, string endError) = RunScout("-o", "needle$", path);
        (int pinnedEndExitCode, byte[] pinnedEndOutput, string pinnedEndError) = RunPinnedRipgrep("-o", "needle$", path);

        Assert.Equal(pinnedStartExitCode, startExitCode);
        Assert.Equal(pinnedStartOutput, startOutput);
        Assert.Equal(pinnedStartError, startError);
        Assert.Equal(pinnedEndExitCode, endExitCode);
        Assert.Equal(pinnedEndOutput, endOutput);
        Assert.Equal(pinnedEndError, endError);
    }

    /// <summary>
    /// Verifies default regex mode supports top-level alternation.
    /// </summary>
    [Fact]
    public void DefaultRegexAlternationMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "cat\ndog\ncow\nhotdog\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "cat|dog", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "cat|dog", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default regex mode supports grouped alternatives and grouped repetition.
    /// </summary>
    [Fact]
    public void DefaultRegexGroupsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foobar\nfoobaz\nfooqux\nfooquux\nabababc\nabc\nabbc\n");

        (int groupExitCode, byte[] groupOutput, string groupError) = RunScout("-n", "foo(ba[rz]|qux)", path);
        (int pinnedGroupExitCode, byte[] pinnedGroupOutput, string pinnedGroupError) = RunPinnedRipgrep("-n", "foo(ba[rz]|qux)", path);
        (int repeatExitCode, byte[] repeatOutput, string repeatError) = RunScout("-o", "(ab)+c", path);
        (int pinnedRepeatExitCode, byte[] pinnedRepeatOutput, string pinnedRepeatError) = RunPinnedRipgrep("-o", "(ab)+c", path);

        Assert.Equal(pinnedGroupExitCode, groupExitCode);
        Assert.Equal(pinnedGroupOutput, groupOutput);
        Assert.Equal(pinnedGroupError, groupError);
        Assert.Equal(pinnedRepeatExitCode, repeatExitCode);
        Assert.Equal(pinnedRepeatOutput, repeatOutput);
        Assert.Equal(pinnedRepeatError, repeatError);
    }

    /// <summary>
    /// Verifies default regex mode supports escaped ASCII character classes and control escapes.
    /// </summary>
    [Fact]
    public void DefaultRegexEscapesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabcXYZ\nname_1\nname-1\nspace tab\nspace\ttab\n123\n");

        (int digitExitCode, byte[] digitOutput, string digitError) = RunScout("-n", @"\d+", path);
        (int pinnedDigitExitCode, byte[] pinnedDigitOutput, string pinnedDigitError) = RunPinnedRipgrep("-n", @"\d+", path);
        (int wordExitCode, byte[] wordOutput, string wordError) = RunScout("-n", @"^\w+$", path);
        (int pinnedWordExitCode, byte[] pinnedWordOutput, string pinnedWordError) = RunPinnedRipgrep("-n", @"^\w+$", path);
        (int nonDigitExitCode, byte[] nonDigitOutput, string nonDigitError) = RunScout("-n", @"\D", path);
        (int pinnedNonDigitExitCode, byte[] pinnedNonDigitOutput, string pinnedNonDigitError) = RunPinnedRipgrep("-n", @"\D", path);
        (int whitespaceExitCode, byte[] whitespaceOutput, string whitespaceError) = RunScout("-n", @"space\stab", path);
        (int pinnedWhitespaceExitCode, byte[] pinnedWhitespaceOutput, string pinnedWhitespaceError) = RunPinnedRipgrep("-n", @"space\stab", path);
        (int tabExitCode, byte[] tabOutput, string tabError) = RunScout("-n", @"space\ttab", path);
        (int pinnedTabExitCode, byte[] pinnedTabOutput, string pinnedTabError) = RunPinnedRipgrep("-n", @"space\ttab", path);

        Assert.Equal(pinnedDigitExitCode, digitExitCode);
        Assert.Equal(pinnedDigitOutput, digitOutput);
        Assert.Equal(pinnedDigitError, digitError);
        Assert.Equal(pinnedWordExitCode, wordExitCode);
        Assert.Equal(pinnedWordOutput, wordOutput);
        Assert.Equal(pinnedWordError, wordError);
        Assert.Equal(pinnedNonDigitExitCode, nonDigitExitCode);
        Assert.Equal(pinnedNonDigitOutput, nonDigitOutput);
        Assert.Equal(pinnedNonDigitError, nonDigitError);
        Assert.Equal(pinnedWhitespaceExitCode, whitespaceExitCode);
        Assert.Equal(pinnedWhitespaceOutput, whitespaceOutput);
        Assert.Equal(pinnedWhitespaceError, whitespaceError);
        Assert.Equal(pinnedTabExitCode, tabExitCode);
        Assert.Equal(pinnedTabOutput, tabOutput);
        Assert.Equal(pinnedTabError, tabError);
    }

    /// <summary>
    /// Verifies default regex mode supports ASCII hex and scalar escapes.
    /// </summary>
    [Fact]
    public void DefaultRegexHexEscapesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\nA-B\nA.B\n");

        (int shortHexExitCode, byte[] shortHexOutput, string shortHexError) = RunScout("-n", @"\x66oo", path);
        (int pinnedShortHexExitCode, byte[] pinnedShortHexOutput, string pinnedShortHexError) = RunPinnedRipgrep("-n", @"\x66oo", path);
        (int bracedHexExitCode, byte[] bracedHexOutput, string bracedHexError) = RunScout("-n", @"\x{62}ar", path);
        (int pinnedBracedHexExitCode, byte[] pinnedBracedHexOutput, string pinnedBracedHexError) = RunPinnedRipgrep("-n", @"\x{62}ar", path);
        (int scalarExitCode, byte[] scalarOutput, string scalarError) = RunScout("-n", @"\u{41}\.B", path);
        (int pinnedScalarExitCode, byte[] pinnedScalarOutput, string pinnedScalarError) = RunPinnedRipgrep("-n", @"\u{41}\.B", path);
        (int punctuationExitCode, byte[] punctuationOutput, string punctuationError) = RunScout("-n", @"\x2d", path);
        (int pinnedPunctuationExitCode, byte[] pinnedPunctuationOutput, string pinnedPunctuationError) = RunPinnedRipgrep("-n", @"\x2d", path);

        Assert.Equal(pinnedShortHexExitCode, shortHexExitCode);
        Assert.Equal(pinnedShortHexOutput, shortHexOutput);
        Assert.Equal(pinnedShortHexError, shortHexError);
        Assert.Equal(pinnedBracedHexExitCode, bracedHexExitCode);
        Assert.Equal(pinnedBracedHexOutput, bracedHexOutput);
        Assert.Equal(pinnedBracedHexError, bracedHexError);
        Assert.Equal(pinnedScalarExitCode, scalarExitCode);
        Assert.Equal(pinnedScalarOutput, scalarOutput);
        Assert.Equal(pinnedScalarError, scalarError);
        Assert.Equal(pinnedPunctuationExitCode, punctuationExitCode);
        Assert.Equal(pinnedPunctuationOutput, punctuationOutput);
        Assert.Equal(pinnedPunctuationError, punctuationError);
    }

    /// <summary>
    /// Verifies default regex mode supports word-boundary assertions.
    /// </summary>
    [Fact]
    public void DefaultRegexWordBoundariesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfoo-bar\nfoo_bar\nbarfoo\nfoo2\nxxfooxx\nbar foo baz\n");

        (int wordExitCode, byte[] wordOutput, string wordError) = RunScout("-n", @"\bfoo\b", path);
        (int pinnedWordExitCode, byte[] pinnedWordOutput, string pinnedWordError) = RunPinnedRipgrep("-n", @"\bfoo\b", path);
        (int leftNonBoundaryExitCode, byte[] leftNonBoundaryOutput, string leftNonBoundaryError) = RunScout("-n", @"\Bfoo", path);
        (int pinnedLeftNonBoundaryExitCode, byte[] pinnedLeftNonBoundaryOutput, string pinnedLeftNonBoundaryError) = RunPinnedRipgrep("-n", @"\Bfoo", path);
        (int rightNonBoundaryExitCode, byte[] rightNonBoundaryOutput, string rightNonBoundaryError) = RunScout("-n", @"foo\B", path);
        (int pinnedRightNonBoundaryExitCode, byte[] pinnedRightNonBoundaryOutput, string pinnedRightNonBoundaryError) = RunPinnedRipgrep("-n", @"foo\B", path);
        (int startExitCode, byte[] startOutput, string startError) = RunScout("-n", @"\b{start}foo", path);
        (int pinnedStartExitCode, byte[] pinnedStartOutput, string pinnedStartError) = RunPinnedRipgrep("-n", @"\b{start}foo", path);
        (int endExitCode, byte[] endOutput, string endError) = RunScout("-n", @"foo\b{end}", path);
        (int pinnedEndExitCode, byte[] pinnedEndOutput, string pinnedEndError) = RunPinnedRipgrep("-n", @"foo\b{end}", path);

        Assert.Equal(pinnedWordExitCode, wordExitCode);
        Assert.Equal(pinnedWordOutput, wordOutput);
        Assert.Equal(pinnedWordError, wordError);
        Assert.Equal(pinnedLeftNonBoundaryExitCode, leftNonBoundaryExitCode);
        Assert.Equal(pinnedLeftNonBoundaryOutput, leftNonBoundaryOutput);
        Assert.Equal(pinnedLeftNonBoundaryError, leftNonBoundaryError);
        Assert.Equal(pinnedRightNonBoundaryExitCode, rightNonBoundaryExitCode);
        Assert.Equal(pinnedRightNonBoundaryOutput, rightNonBoundaryOutput);
        Assert.Equal(pinnedRightNonBoundaryError, rightNonBoundaryError);
        Assert.Equal(pinnedStartExitCode, startExitCode);
        Assert.Equal(pinnedStartOutput, startOutput);
        Assert.Equal(pinnedStartError, startError);
        Assert.Equal(pinnedEndExitCode, endExitCode);
        Assert.Equal(pinnedEndOutput, endOutput);
        Assert.Equal(pinnedEndError, endError);
    }

    /// <summary>
    /// Verifies default regex mode matches a combined syntax pattern pinned against ripgrep.
    /// </summary>
    [Fact]
    public void DefaultRegexCombinedSyntaxMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc12\nabc123\nname_1\nabcXYZ\n");

        const string pattern = @"(?P<word>[[:alpha:]]+)(?:\d{2,3}?|_\w+)\b{end}";
        (int exitCode, byte[] output, string error) = RunScout("-n", "-o", pattern, path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-o", pattern, path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies literal newlines in patterns require multiline mode like ripgrep.
    /// </summary>
    [Fact]
    public void PatternNewlineRequiresMultilineMode()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\n");

        (int exitCode, byte[] output, string error) = RunScout("foo\nbar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("foo\nbar", path);
        (int fixedExitCode, byte[] fixedOutput, string fixedError) = RunScout("-F", "foo\nbar", path);
        (int pinnedFixedExitCode, byte[] pinnedFixedOutput, string pinnedFixedError) = RunPinnedRipgrep("-F", "foo\nbar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedFixedExitCode, fixedExitCode);
        Assert.Equal(pinnedFixedOutput, fixedOutput);
        Assert.Equal(pinnedFixedError, fixedError);
    }

    /// <summary>
    /// Verifies multiline mode can match across line boundaries for basic file search modes.
    /// </summary>
    [Fact]
    public void MultilineSearchMatchesAcrossLineBoundaries()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "pre\nxxfoo\nbarzz\npost\n");

        (int standardExitCode, byte[] standardOutput, string standardError) = RunScout("-U", "foo\nbar", path);
        (int pinnedStandardExitCode, byte[] pinnedStandardOutput, string pinnedStandardError) = RunPinnedRipgrep("-U", "foo\nbar", path);
        (int lineExitCode, byte[] lineOutput, string lineError) = RunScout("-n", "--column", "-U", "foo\nbar", path);
        (int pinnedLineExitCode, byte[] pinnedLineOutput, string pinnedLineError) = RunPinnedRipgrep("-n", "--column", "-U", "foo\nbar", path);
        (int onlyExitCode, byte[] onlyOutput, string onlyError) = RunScout("-n", "-o", "-U", "foo\nbar", path);
        (int pinnedOnlyExitCode, byte[] pinnedOnlyOutput, string pinnedOnlyError) = RunPinnedRipgrep("-n", "-o", "-U", "foo\nbar", path);
        (int onlyColumnExitCode, byte[] onlyColumnOutput, string onlyColumnError) = RunScout("-n", "--column", "-o", "-U", "foo\nbar", path);
        (int pinnedOnlyColumnExitCode, byte[] pinnedOnlyColumnOutput, string pinnedOnlyColumnError) = RunPinnedRipgrep("-n", "--column", "-o", "-U", "foo\nbar", path);
        (int countExitCode, byte[] countOutput, string countError) = RunScout("-c", "-U", "foo\nbar", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("-c", "-U", "foo\nbar", path);
        (int filesExitCode, byte[] filesOutput, string filesError) = RunScout("-l", "-U", "foo\nbar", path);
        (int pinnedFilesExitCode, byte[] pinnedFilesOutput, string pinnedFilesError) = RunPinnedRipgrep("-l", "-U", "foo\nbar", path);
        (int vimgrepExitCode, byte[] vimgrepOutput, string vimgrepError) = RunScout("--vimgrep", "-U", "foo\nbar", path);
        (int pinnedVimgrepExitCode, byte[] pinnedVimgrepOutput, string pinnedVimgrepError) = RunPinnedRipgrep("--vimgrep", "-U", "foo\nbar", path);

        Assert.Equal(pinnedStandardExitCode, standardExitCode);
        Assert.Equal(pinnedStandardOutput, standardOutput);
        Assert.Equal(pinnedStandardError, standardError);
        Assert.Equal(pinnedLineExitCode, lineExitCode);
        Assert.Equal(pinnedLineOutput, lineOutput);
        Assert.Equal(pinnedLineError, lineError);
        Assert.Equal(pinnedOnlyExitCode, onlyExitCode);
        Assert.Equal(pinnedOnlyOutput, onlyOutput);
        Assert.Equal(pinnedOnlyError, onlyError);
        Assert.Equal(pinnedOnlyColumnExitCode, onlyColumnExitCode);
        Assert.Equal(pinnedOnlyColumnOutput, onlyColumnOutput);
        Assert.Equal(pinnedOnlyColumnError, onlyColumnError);
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
        Assert.Equal(pinnedFilesExitCode, filesExitCode);
        Assert.Equal(pinnedFilesOutput, filesOutput);
        Assert.Equal(pinnedFilesError, filesError);
        Assert.Equal(pinnedVimgrepExitCode, vimgrepExitCode);
        Assert.Equal(pinnedVimgrepOutput, vimgrepOutput);
        Assert.Equal(pinnedVimgrepError, vimgrepError);
    }

    /// <summary>
    /// Verifies multiline mode keeps ordinary line-search anchor semantics without a literal newline pattern.
    /// </summary>
    [Fact]
    public void MultilineModeWithoutLiteralNewlineUsesLineSearch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-U", "$", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-U", "$", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies multiline mode uses multiline regex anchors inside whole-buffer searches.
    /// </summary>
    [Fact]
    public void MultilineSearchUsesMultilineAnchors()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-U", "foo\n^bar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-U", "foo\n^bar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies multiline dot-all mode lets dot span line boundaries and remains overridable.
    /// </summary>
    [Fact]
    public void MultilineDotallMatchesAcrossLineBoundaries()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\n");

        (int noMultilineExitCode, byte[] noMultilineOutput, string noMultilineError) = RunScout("--multiline-dotall", "foo.*bar", path);
        (int pinnedNoMultilineExitCode, byte[] pinnedNoMultilineOutput, string pinnedNoMultilineError) = RunPinnedRipgrep("--multiline-dotall", "foo.*bar", path);
        (int dotallExitCode, byte[] dotallOutput, string dotallError) = RunScout("-n", "-U", "--multiline-dotall", "foo.*bar", path);
        (int pinnedDotallExitCode, byte[] pinnedDotallOutput, string pinnedDotallError) = RunPinnedRipgrep("-n", "-U", "--multiline-dotall", "foo.*bar", path);
        (int inlineDotallExitCode, byte[] inlineDotallOutput, string inlineDotallError) = RunScout("-n", "-U", "(?s)foo.*bar", path);
        (int pinnedInlineDotallExitCode, byte[] pinnedInlineDotallOutput, string pinnedInlineDotallError) = RunPinnedRipgrep("-n", "-U", "(?s)foo.*bar", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-n", "-U", "--multiline-dotall", "(?-s:foo.*bar)", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-n", "-U", "--multiline-dotall", "(?-s:foo.*bar)", path);

        Assert.Equal(pinnedNoMultilineExitCode, noMultilineExitCode);
        Assert.Equal(pinnedNoMultilineOutput, noMultilineOutput);
        Assert.Equal(pinnedNoMultilineError, noMultilineError);
        Assert.Equal(pinnedDotallExitCode, dotallExitCode);
        Assert.Equal(pinnedDotallOutput, dotallOutput);
        Assert.Equal(pinnedDotallError, dotallError);
        Assert.Equal(pinnedInlineDotallExitCode, inlineDotallExitCode);
        Assert.Equal(pinnedInlineDotallOutput, inlineDotallOutput);
        Assert.Equal(pinnedInlineDotallError, inlineDotallError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
    }

    /// <summary>
    /// Verifies JSON output represents multiline matches with ripgrep-compatible line ranges and submatches.
    /// </summary>
    [Fact]
    public void MultilineJsonMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "pre\nxxfoo\nbarzz\npost\n");

        (int newlineExitCode, byte[] newlineOutput, string newlineError) = RunScout("--json", "-U", "foo\nbar", path);
        (int pinnedNewlineExitCode, byte[] pinnedNewlineOutput, string pinnedNewlineError) = RunPinnedRipgrep("--json", "-U", "foo\nbar", path);
        (int anchorExitCode, byte[] anchorOutput, string anchorError) = RunScout("--json", "-U", "foo\n^bar", path);
        (int pinnedAnchorExitCode, byte[] pinnedAnchorOutput, string pinnedAnchorError) = RunPinnedRipgrep("--json", "-U", "foo\n^bar", path);
        (int dotallExitCode, byte[] dotallOutput, string dotallError) = RunScout("--json", "-U", "--multiline-dotall", "foo.*bar", path);
        (int pinnedDotallExitCode, byte[] pinnedDotallOutput, string pinnedDotallError) = RunPinnedRipgrep("--json", "-U", "--multiline-dotall", "foo.*bar", path);

        Assert.Equal(pinnedNewlineExitCode, newlineExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedNewlineOutput), NormalizeJsonTimings(newlineOutput));
        Assert.Equal(pinnedNewlineError, newlineError);
        Assert.Equal(pinnedAnchorExitCode, anchorExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedAnchorOutput), NormalizeJsonTimings(anchorOutput));
        Assert.Equal(pinnedAnchorError, anchorError);
        Assert.Equal(pinnedDotallExitCode, dotallExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedDotallOutput), NormalizeJsonTimings(dotallOutput));
        Assert.Equal(pinnedDotallError, dotallError);
    }

    /// <summary>
    /// Verifies multiline regex search honors case-insensitive mode and scoped disabling.
    /// </summary>
    [Fact]
    public void MultilineCaseInsensitiveMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xxFOO\nBARzz\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-i", "-U", "foo\nbar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-i", "-U", "foo\nbar", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-n", "-i", "-U", "(?-i:foo)\nbar", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-n", "-i", "-U", "(?-i:foo)\nbar", path);
        (int jsonExitCode, byte[] jsonOutput, string jsonError) = RunScout("--json", "-i", "-U", "foo\nbar", path);
        (int pinnedJsonExitCode, byte[] pinnedJsonOutput, string pinnedJsonError) = RunPinnedRipgrep("--json", "-i", "-U", "foo\nbar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
        Assert.Equal(pinnedJsonExitCode, jsonExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedJsonOutput), NormalizeJsonTimings(jsonOutput));
        Assert.Equal(pinnedJsonError, jsonError);
    }

    /// <summary>
    /// Verifies multiline regex search honors word-regexp boundaries.
    /// </summary>
    [Fact]
    public void MultilineWordRegexpMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xfoo\nbarz\nxx foo\nbar yy\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-w", "-U", "foo\nbar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-w", "-U", "foo\nbar", path);
        (int insensitiveExitCode, byte[] insensitiveOutput, string insensitiveError) = RunScout("-n", "-w", "-i", "-U", "FOO\nBAR", path);
        (int pinnedInsensitiveExitCode, byte[] pinnedInsensitiveOutput, string pinnedInsensitiveError) = RunPinnedRipgrep("-n", "-w", "-i", "-U", "FOO\nBAR", path);
        (int jsonExitCode, byte[] jsonOutput, string jsonError) = RunScout("--json", "-w", "-U", "foo\nbar", path);
        (int pinnedJsonExitCode, byte[] pinnedJsonOutput, string pinnedJsonError) = RunPinnedRipgrep("--json", "-w", "-U", "foo\nbar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedInsensitiveExitCode, insensitiveExitCode);
        Assert.Equal(pinnedInsensitiveOutput, insensitiveOutput);
        Assert.Equal(pinnedInsensitiveError, insensitiveError);
        Assert.Equal(pinnedJsonExitCode, jsonExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedJsonOutput), NormalizeJsonTimings(jsonOutput));
        Assert.Equal(pinnedJsonError, jsonError);
    }

    /// <summary>
    /// Verifies multiline regex search honors line-regexp boundaries.
    /// </summary>
    [Fact]
    public void MultilineLineRegexpMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xxfoo\nbarzz\nfoo\nbar\nfoo\nbar baz\nFOO\nBAR\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-x", "-U", "foo\nbar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-x", "-U", "foo\nbar", path);
        (int insensitiveExitCode, byte[] insensitiveOutput, string insensitiveError) = RunScout("-n", "-x", "-i", "-U", "FOO\nBAR", path);
        (int pinnedInsensitiveExitCode, byte[] pinnedInsensitiveOutput, string pinnedInsensitiveError) = RunPinnedRipgrep("-n", "-x", "-i", "-U", "FOO\nBAR", path);
        (int onlyExitCode, byte[] onlyOutput, string onlyError) = RunScout("-n", "--column", "-o", "-x", "-U", "foo\nbar", path);
        (int pinnedOnlyExitCode, byte[] pinnedOnlyOutput, string pinnedOnlyError) = RunPinnedRipgrep("-n", "--column", "-o", "-x", "-U", "foo\nbar", path);
        (int vimgrepExitCode, byte[] vimgrepOutput, string vimgrepError) = RunScout("--vimgrep", "-x", "-U", "foo\nbar", path);
        (int pinnedVimgrepExitCode, byte[] pinnedVimgrepOutput, string pinnedVimgrepError) = RunPinnedRipgrep("--vimgrep", "-x", "-U", "foo\nbar", path);
        (int jsonExitCode, byte[] jsonOutput, string jsonError) = RunScout("--json", "-x", "-U", "foo\nbar", path);
        (int pinnedJsonExitCode, byte[] pinnedJsonOutput, string pinnedJsonError) = RunPinnedRipgrep("--json", "-x", "-U", "foo\nbar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedInsensitiveExitCode, insensitiveExitCode);
        Assert.Equal(pinnedInsensitiveOutput, insensitiveOutput);
        Assert.Equal(pinnedInsensitiveError, insensitiveError);
        Assert.Equal(pinnedOnlyExitCode, onlyExitCode);
        Assert.Equal(pinnedOnlyOutput, onlyOutput);
        Assert.Equal(pinnedOnlyError, onlyError);
        Assert.Equal(pinnedVimgrepExitCode, vimgrepExitCode);
        Assert.Equal(pinnedVimgrepOutput, vimgrepOutput);
        Assert.Equal(pinnedVimgrepError, vimgrepError);
        Assert.Equal(pinnedJsonExitCode, jsonExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedJsonOutput), NormalizeJsonTimings(jsonOutput));
        Assert.Equal(pinnedJsonError, jsonError);
    }

    /// <summary>
    /// Verifies multiline regex search honors inverted line selection.
    /// </summary>
    [Fact]
    public void MultilineInvertMatchMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "pre\nfoo\nbar\nmid\nfoo\nbar baz\npost\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-v", "-U", "foo\nbar", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-v", "-U", "foo\nbar", path);
        (int countExitCode, byte[] countOutput, string countError) = RunScout("-c", "-v", "-U", "foo\nbar", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("-c", "-v", "-U", "foo\nbar", path);
        (int countMatchesExitCode, byte[] countMatchesOutput, string countMatchesError) = RunScout("--count-matches", "-v", "-U", "foo\nbar", path);
        (int pinnedCountMatchesExitCode, byte[] pinnedCountMatchesOutput, string pinnedCountMatchesError) = RunPinnedRipgrep("--count-matches", "-v", "-U", "foo\nbar", path);
        (int onlyExitCode, byte[] onlyOutput, string onlyError) = RunScout("-n", "-o", "-v", "-U", "foo\nbar", path);
        (int pinnedOnlyExitCode, byte[] pinnedOnlyOutput, string pinnedOnlyError) = RunPinnedRipgrep("-n", "-o", "-v", "-U", "foo\nbar", path);
        (int vimgrepExitCode, byte[] vimgrepOutput, string vimgrepError) = RunScout("--vimgrep", "-v", "-U", "foo\nbar", path);
        (int pinnedVimgrepExitCode, byte[] pinnedVimgrepOutput, string pinnedVimgrepError) = RunPinnedRipgrep("--vimgrep", "-v", "-U", "foo\nbar", path);
        (int lineRegexpExitCode, byte[] lineRegexpOutput, string lineRegexpError) = RunScout("-n", "-x", "-v", "-U", "foo\nbar", path);
        (int pinnedLineRegexpExitCode, byte[] pinnedLineRegexpOutput, string pinnedLineRegexpError) = RunPinnedRipgrep("-n", "-x", "-v", "-U", "foo\nbar", path);
        (int jsonExitCode, byte[] jsonOutput, string jsonError) = RunScout("--json", "-v", "-U", "foo\nbar", path);
        (int pinnedJsonExitCode, byte[] pinnedJsonOutput, string pinnedJsonError) = RunPinnedRipgrep("--json", "-v", "-U", "foo\nbar", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
        Assert.Equal(pinnedCountMatchesExitCode, countMatchesExitCode);
        Assert.Equal(pinnedCountMatchesOutput, countMatchesOutput);
        Assert.Equal(pinnedCountMatchesError, countMatchesError);
        Assert.Equal(pinnedOnlyExitCode, onlyExitCode);
        Assert.Equal(pinnedOnlyOutput, onlyOutput);
        Assert.Equal(pinnedOnlyError, onlyError);
        Assert.Equal(pinnedVimgrepExitCode, vimgrepExitCode);
        Assert.Equal(pinnedVimgrepOutput, vimgrepOutput);
        Assert.Equal(pinnedVimgrepError, vimgrepError);
        Assert.Equal(pinnedLineRegexpExitCode, lineRegexpExitCode);
        Assert.Equal(pinnedLineRegexpOutput, lineRegexpOutput);
        Assert.Equal(pinnedLineRegexpError, lineRegexpError);
        Assert.Equal(pinnedJsonExitCode, jsonExitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedJsonOutput), NormalizeJsonTimings(jsonOutput));
        Assert.Equal(pinnedJsonError, jsonError);
    }

    /// <summary>
    /// Verifies line-regexp mode applies default regex semantics to full-line matches.
    /// </summary>
    [Fact]
    public void LineRegexpDefaultRegexMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "a.c\nabc\na-c\nxxabc\n");

        (int dotExitCode, byte[] dotOutput, string dotError) = RunScout("-x", "a.c", path);
        (int pinnedDotExitCode, byte[] pinnedDotOutput, string pinnedDotError) = RunPinnedRipgrep("-x", "a.c", path);
        (int classExitCode, byte[] classOutput, string classError) = RunScout("-x", "[ab]+c", path);
        (int pinnedClassExitCode, byte[] pinnedClassOutput, string pinnedClassError) = RunPinnedRipgrep("-x", "[ab]+c", path);
        (int fixedExitCode, byte[] fixedOutput, string fixedError) = RunScout("-F", "-x", "a.c", path);
        (int pinnedFixedExitCode, byte[] pinnedFixedOutput, string pinnedFixedError) = RunPinnedRipgrep("-F", "-x", "a.c", path);

        Assert.Equal(pinnedDotExitCode, dotExitCode);
        Assert.Equal(pinnedDotOutput, dotOutput);
        Assert.Equal(pinnedDotError, dotError);
        Assert.Equal(pinnedClassExitCode, classExitCode);
        Assert.Equal(pinnedClassOutput, classOutput);
        Assert.Equal(pinnedClassError, classError);
        Assert.Equal(pinnedFixedExitCode, fixedExitCode);
        Assert.Equal(pinnedFixedOutput, fixedOutput);
        Assert.Equal(pinnedFixedError, fixedError);
    }

    /// <summary>
    /// Verifies quiet mode suppresses matching output and returns success on a match.
    /// </summary>
    [Fact]
    public void QuietSuppressesMatchingOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-q"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies quiet mode returns no-match without output when no line matches.
    /// </summary>
    [Fact]
    public void QuietReturnsNoMatchWithoutOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--quiet"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--quiet", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies quiet mode suppresses count output while preserving match status.
    /// </summary>
    [Fact]
    public void QuietSuppressesCountOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-q"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies invert-match mode prints nonmatching lines.
    /// </summary>
    [Fact]
    public void InvertMatchPrintsNonMatchingLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nmiss\nneedle two needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-v", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies invert-match mode treats count-matches as an inverted line count.
    /// </summary>
    [Fact]
    public void InvertMatchCountMatchesCountsInvertedLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss one\nmiss two\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-v", "--count-matches", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies invert-match file summaries use inverted-line match status.
    /// </summary>
    [Fact]
    public void InvertMatchFilesWithoutMatchUsesInvertedStatus()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("--files-without-match"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-v", "--files-without-match", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies line-regexp mode matches only full-line literals.
    /// </summary>
    [Fact]
    public void LineRegexpMatchesOnlyFullLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle suffix\nprefix needle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-x", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies line-regexp count-matches counts matching lines.
    /// </summary>
    [Fact]
    public void LineRegexpCountMatchesCountsFullLineMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle suffix\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-x", "--count-matches", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies line-regexp works with inverted line matching.
    /// </summary>
    [Fact]
    public void LineRegexpInvertMatchPrintsNonFullLineMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle suffix\nprefix needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-x", "-v", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies word-regexp mode matches literals only at word boundaries.
    /// </summary>
    [Fact]
    public void WordRegexpMatchesOnlyWholeWords()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfoobar\nbarfoo\nfoo_bar\nfoo-bar\nx foo y\nfoo2\n2foo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-w", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies word-regexp count-matches counts whole-word occurrences.
    /// </summary>
    [Fact]
    public void WordRegexpCountMatchesCountsWholeWordOccurrences()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo foo2 foo foo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-w", "--count-matches", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies word-regexp works with inverted line matching.
    /// </summary>
    [Fact]
    public void WordRegexpInvertMatchPrintsNonWholeWordMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfoobar\nbarfoo\nfoo_bar\nfoo-bar\nx foo y\nfoo2\n2foo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-w", "-v", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies word-regexp and line-regexp use ripgrep's last-wins behavior.
    /// </summary>
    [Fact]
    public void WordRegexpAndLineRegexpUseLastWinsBehavior()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfoo-bar\nfoo bar\n");
        using MemoryStream wordWinsOutput = new();
        using MemoryStream wordWinsError = new();
        using MemoryStream lineWinsOutput = new();
        using MemoryStream lineWinsError = new();
        var wordWinsOutputWriter = new RawByteWriter(wordWinsOutput);
        var wordWinsErrorWriter = new RawByteWriter(wordWinsError);
        var lineWinsOutputWriter = new RawByteWriter(lineWinsOutput);
        var lineWinsErrorWriter = new RawByteWriter(lineWinsError);
        OsString[] wordWinsArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];
        OsString[] lineWinsArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int wordWinsExitCode = ScoutApplication.Run(wordWinsArguments, wordWinsOutputWriter, wordWinsErrorWriter);
        int lineWinsExitCode = ScoutApplication.Run(lineWinsArguments, lineWinsOutputWriter, lineWinsErrorWriter);
        (int pinnedWordExitCode, byte[] pinnedWordOutput, string pinnedWordError) = RunPinnedRipgrep("-x", "-w", "foo", path);
        (int pinnedLineExitCode, byte[] pinnedLineOutput, string pinnedLineError) = RunPinnedRipgrep("-w", "-x", "foo", path);

        Assert.Equal(pinnedWordExitCode, wordWinsExitCode);
        Assert.Equal(pinnedWordOutput, wordWinsOutput.ToArray());
        Assert.Equal(pinnedWordError, Utf8(wordWinsError.ToArray()));
        Assert.Equal(pinnedLineExitCode, lineWinsExitCode);
        Assert.Equal(pinnedLineOutput, lineWinsOutput.ToArray());
        Assert.Equal(pinnedLineError, Utf8(lineWinsError.ToArray()));
    }

    /// <summary>
    /// Verifies count mode prints the number of matching lines.
    /// </summary>
    [Fact]
    public void LiteralSearchCountPrintsMatchingLineCount()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nmiss\nneedle two needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("2\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies count mode omits files with no matches and returns no-match.
    /// </summary>
    [Fact]
    public void LiteralSearchCountOmitsZeroCounts()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--count"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies include-zero prints zero count output while preserving no-match status.
    /// </summary>
    [Fact]
    public void IncludeZeroCountPrintsZeroForUnmatchedFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--include-zero"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--include-zero", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies include-zero applies to count-matches with filename prefixes.
    /// </summary>
    [Fact]
    public void IncludeZeroCountMatchesPrintsZeroForUnmatchedFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--include-zero"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--include-zero", "-H", "--count-matches", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies no-include-zero disables earlier zero count output.
    /// </summary>
    [Fact]
    public void NoIncludeZeroDisablesEarlierIncludeZero()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--include-zero"u8),
            OsString.FromUnixBytes("--no-include-zero"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--include-zero", "--no-include-zero", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies count-matches mode counts non-overlapping literal occurrences.
    /// </summary>
    [Fact]
    public void LiteralSearchCountMatchesPrintsMatchCount()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nneedle two needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("3\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies ignore-case mode matches ASCII case variants.
    /// </summary>
    [Fact]
    public void LiteralSearchIgnoreCaseMatchesAsciiCaseVariants()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "Needle upper\nneedle lower\nNEEDLE all\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-i"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("Needle upper\nneedle lower\nNEEDLE all\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies case-sensitive mode overrides earlier ignore-case mode.
    /// </summary>
    [Fact]
    public void LiteralSearchCaseSensitiveOverridesIgnoreCase()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "Needle upper\nneedle lower\nNEEDLE all\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-i"u8),
            OsString.FromUnixBytes("-s"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("needle lower\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies smart-case mode uses insensitive matching for lowercase ASCII patterns.
    /// </summary>
    [Fact]
    public void LiteralSearchSmartCaseUsesInsensitiveForLowercasePattern()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "Needle upper\nneedle lower\nNEEDLE all\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--smart-case"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("Needle upper\nneedle lower\nNEEDLE all\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies smart-case mode uses sensitive matching for uppercase ASCII patterns.
    /// </summary>
    [Fact]
    public void LiteralSearchSmartCaseUsesSensitiveForUppercasePattern()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "Needle upper\nneedle lower\nNEEDLE all\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--smart-case"u8),
            OsString.FromUnixBytes("Needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("Needle upper\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies ignore-case mode affects count-matches output.
    /// </summary>
    [Fact]
    public void LiteralSearchIgnoreCaseAffectsCountMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "Needle upper\nneedle lower\nNEEDLE all\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-i"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("3\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies count mode overrides line-number printing.
    /// </summary>
    [Fact]
    public void LiteralSearchCountOverridesLineNumbers()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle one\nmiss\nneedle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("2\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies a literal search returns the no-match exit code.
    /// </summary>
    [Fact]
    public void LiteralSearchReturnsNoMatchWhenPatternIsAbsent()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies multiple searched files are printed with path prefixes.
    /// </summary>
    [Fact]
    public void LiteralSearchPrefixesMultipleFiles()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\nmiss\n");
        File.WriteAllText(second, "needle second");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{first}:needle first\n{second}:needle second\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies with-filename forces a path prefix for one searched file.
    /// </summary>
    [Fact]
    public void WithFilenamePrefixesSingleFileMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--with-filename"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--with-filename", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies heading mode prints a file header instead of repeating the path prefix.
    /// </summary>
    [Fact]
    public void HeadingPrintsSingleFileHeaderWhenFilenameIsEnabled()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one\nneedle two\n");

        (int exitCode, byte[] output, string error) = RunScout("--heading", "-H", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--heading", "-H", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies no-heading disables a preceding heading flag.
    /// </summary>
    [Fact]
    public void NoHeadingDisablesEarlierHeading()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--heading", "--no-heading", "-H", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--heading", "--no-heading", "-H", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies heading mode groups sorted directory matches by file.
    /// </summary>
    [Fact]
    public void HeadingGroupsDirectoryMatchesByFile()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "a.txt"), "miss\nneedle a\n");
        File.WriteAllText(Path.Combine(root, "b.txt"), "needle b\nmiss\nneedle again\n");

        (int exitCode, byte[] output, string error) = RunScout("--sort=path", "--heading", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort=path", "--heading", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies heading mode keeps inter-file spacing even when filenames are suppressed.
    /// </summary>
    [Fact]
    public void HeadingNoFilenameSeparatesFileOutput()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "a.txt"), "needle a\n");
        File.WriteAllText(Path.Combine(root, "b.txt"), "needle b\n");

        (int exitCode, byte[] output, string error) = RunScout("--sort=path", "--heading", "--no-filename", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort=path", "--heading", "--no-filename", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies null mode terminates heading path headers with NUL bytes.
    /// </summary>
    [Fact]
    public void HeadingUsesNullPathTerminatorForHeaders()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--heading", "-0", "-H", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--heading", "-0", "-H", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies no-filename suppresses automatic path prefixes for multiple searched files.
    /// </summary>
    [Fact]
    public void NoFilenameSuppressesMultipleFileMatchPrefixes()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\n");
        File.WriteAllText(second, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-I"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-I", "needle", first, second);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies filename flags control count prefixes.
    /// </summary>
    [Fact]
    public void FilenameFlagsControlCountPrefixes()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle\n");
        File.WriteAllText(second, "needle\n");
        using MemoryStream withOutput = new();
        using MemoryStream withError = new();
        using MemoryStream withoutOutput = new();
        using MemoryStream withoutError = new();
        var withOutputWriter = new RawByteWriter(withOutput);
        var withErrorWriter = new RawByteWriter(withError);
        var withoutOutputWriter = new RawByteWriter(withoutOutput);
        var withoutErrorWriter = new RawByteWriter(withoutError);
        OsString[] withArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
        ];
        OsString[] withoutArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--no-filename"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int withExitCode = ScoutApplication.Run(withArguments, withOutputWriter, withErrorWriter);
        int withoutExitCode = ScoutApplication.Run(withoutArguments, withoutOutputWriter, withoutErrorWriter);
        (int pinnedWithExitCode, byte[] pinnedWithOutput, string pinnedWithError) = RunPinnedRipgrep("-H", "-c", "needle", first);
        (int pinnedWithoutExitCode, byte[] pinnedWithoutOutput, string pinnedWithoutError) = RunPinnedRipgrep("--no-filename", "-c", "needle", first, second);

        Assert.Equal(pinnedWithExitCode, withExitCode);
        Assert.Equal(pinnedWithOutput, withOutput.ToArray());
        Assert.Equal(pinnedWithError, Utf8(withError.ToArray()));
        Assert.Equal(pinnedWithoutExitCode, withoutExitCode);
        Assert.Equal(pinnedWithoutOutput, withoutOutput.ToArray());
        Assert.Equal(pinnedWithoutError, Utf8(withoutError.ToArray()));
    }

    /// <summary>
    /// Verifies no-filename does not suppress file-list summary output.
    /// </summary>
    [Fact]
    public void NoFilenameDoesNotSuppressFilesWithMatchesOutput()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle\n");
        File.WriteAllText(second, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--no-filename"u8),
            OsString.FromUnixBytes("-l"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-filename", "-l", "needle", first, second);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies path separator rewrites standard output path prefixes.
    /// </summary>
    [Fact]
    public void PathSeparatorRewritesStandardPathPrefixes()
    {
        string root = CreateTempDirectory();
        string child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        string path = Path.Combine(child, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--path-separator", "Z", "-H", "-n", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--path-separator", "Z", "-H", "-n", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies path separator rewrites summary mode path prefixes.
    /// </summary>
    [Fact]
    public void PathSeparatorRewritesCountPrefixes()
    {
        string root = CreateTempDirectory();
        string child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        string path = Path.Combine(child, "input.txt");
        File.WriteAllText(path, "needle\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("--path-separator=Z", "-H", "-c", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--path-separator=Z", "-H", "-c", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies path separator rewrites files mode output.
    /// </summary>
    [Fact]
    public void PathSeparatorRewritesFilesOutput()
    {
        string root = CreateTempDirectory();
        string child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        string path = Path.Combine(child, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--path-separator", "Z", "--files", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--path-separator", "Z", "--files", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies empty path separator resets path printing to platform defaults.
    /// </summary>
    [Fact]
    public void EmptyPathSeparatorResetsPathPrinting()
    {
        string root = CreateTempDirectory();
        string child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        string path = Path.Combine(child, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--path-separator", "Z", "--path-separator", string.Empty, "-H", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--path-separator", "Z", "--path-separator", string.Empty, "-H", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies no-filename suppresses automatic directory-search prefixes.
    /// </summary>
    [Fact]
    public void NoFilenameSuppressesDirectorySearchPrefixes()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "child"));
        File.WriteAllText(Path.Combine(root, "child", "keep.txt"), "needle child\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--no-filename"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--no-filename", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies byte-offset mode prints line-start offsets for matching lines.
    /// </summary>
    [Fact]
    public void ByteOffsetPrintsLineStartOffsets()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one\nxx needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies byte-offset mode combines with line numbers and explicit filenames.
    /// </summary>
    [Fact]
    public void ByteOffsetCombinesWithLineNumbersAndFilenamePrefixes()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one\nxx needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-H", "-n", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies no-byte-offset disables an earlier byte-offset flag.
    /// </summary>
    [Fact]
    public void NoByteOffsetDisablesEarlierByteOffset()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--byte-offset"u8),
            OsString.FromUnixBytes("--no-byte-offset"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--byte-offset", "--no-byte-offset", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies byte-offset mode does not affect summary count output.
    /// </summary>
    [Fact]
    public void ByteOffsetDoesNotAffectCountOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-b", "--count-matches", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies column mode prints one-based byte columns and implied line numbers.
    /// </summary>
    [Fact]
    public void ColumnPrintsFirstMatchColumnAndImpliesLineNumbers()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nxx needle one\nneedle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--column", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies column output combines with filename and byte-offset fields in ripgrep order.
    /// </summary>
    [Fact]
    public void ColumnCombinesWithFilenameAndByteOffsetFields()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nxx needle one\nneedle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-H", "-n", "--column", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies no-line-number suppresses column's implied line-number field.
    /// </summary>
    [Fact]
    public void NoLineNumberSuppressesColumnImpliedLineNumber()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xx needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-N"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-N", "--column", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies inverted column mode prints line numbers without a column field.
    /// </summary>
    [Fact]
    public void ColumnInvertMatchOmitsColumnField()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\nxx foo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-v", "--column", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies no-column disables an earlier column flag.
    /// </summary>
    [Fact]
    public void NoColumnDisablesEarlierColumn()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("--no-column"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--column", "--no-column", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies column mode does not affect summary count output.
    /// </summary>
    [Fact]
    public void ColumnDoesNotAffectCountOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--column", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies trim mode removes leading ASCII whitespace from printed matching lines.
    /// </summary>
    [Fact]
    public void TrimRemovesLeadingWhitespaceFromMatchingLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "  needle one\n\t needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--trim"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--trim", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies trim mode preserves prefix fields and original match positions.
    /// </summary>
    [Fact]
    public void TrimPreservesPrefixFieldsAndOffsets()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "  needle one\n\t needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--trim"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--trim", "-H", "-n", "--column", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies trim mode also trims only-matching output.
    /// </summary>
    [Fact]
    public void TrimRemovesLeadingWhitespaceFromOnlyMatchingOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "  needle one\n\t needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--trim"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes(" needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--trim", "-o", " needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies no-trim disables an earlier trim flag.
    /// </summary>
    [Fact]
    public void NoTrimDisablesEarlierTrim()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "  needle one\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--trim"u8),
            OsString.FromUnixBytes("--no-trim"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--trim", "--no-trim", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies context mode prints surrounding lines with ripgrep field separators.
    /// </summary>
    [Fact]
    public void ContextPrintsSurroundingLinesWithStandardFields()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "one\ntwo\nneedle\nfour\nfive\nneedle2\nseven\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("-C1"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-H", "-n", "--column", "-b", "-C1", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies non-heading directory context output separates file groups.
    /// </summary>
    [Fact]
    public void DirectoryContextSeparatesFileGroups()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "a.txt");
        string second = Path.Combine(root, "b.txt");
        File.WriteAllText(first, "one\nneedle\ntwo\n");
        File.WriteAllText(second, "three\nneedle\nfour\n");

        (int exitCode, byte[] output, string error) = RunScout("--sort=path", "--no-heading", "-H", "-C1", "needle", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort=path", "--no-heading", "-H", "-C1", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies custom match field separators apply to standard match prefixes.
    /// </summary>
    [Fact]
    public void CustomFieldMatchSeparatorAppliesToStandardFields()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nxx needle\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout(
            "-H",
            "-n",
            "--column",
            "-b",
            "--field-match-separator",
            "|",
            "needle",
            path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "-H",
            "-n",
            "--column",
            "-b",
            "--field-match-separator",
            "|",
            "needle",
            path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies custom context field and group separators match pinned ripgrep output.
    /// </summary>
    [Fact]
    public void CustomContextSeparatorsApplyToContextOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "one\nneedle\ntwo\nthree\nfour\nneedle\nsix\n");

        (int exitCode, byte[] output, string error) = RunScout(
            "-H",
            "-n",
            "-C1",
            "--field-context-separator",
            "|",
            "--context-separator",
            "XX",
            "needle",
            path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "-H",
            "-n",
            "-C1",
            "--field-context-separator",
            "|",
            "--context-separator",
            "XX",
            "needle",
            path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies context group separators can be empty or disabled.
    /// </summary>
    [Fact]
    public void ContextSeparatorCanBeEmptyOrDisabled()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "one\nneedle\ntwo\nthree\nfour\nneedle\nsix\n");

        (int emptyExitCode, byte[] emptyOutput, string emptyError) = RunScout("-H", "-n", "-C1", "--context-separator", string.Empty, "needle", path);
        (int pinnedEmptyExitCode, byte[] pinnedEmptyOutput, string pinnedEmptyError) = RunPinnedRipgrep("-H", "-n", "-C1", "--context-separator", string.Empty, "needle", path);
        (int disabledExitCode, byte[] disabledOutput, string disabledError) = RunScout("-H", "-n", "-C1", "--context-separator", "XX", "--no-context-separator", "needle", path);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep("-H", "-n", "-C1", "--context-separator", "XX", "--no-context-separator", "needle", path);
        (int reenabledExitCode, byte[] reenabledOutput, string reenabledError) = RunScout("-H", "-n", "-C1", "--no-context-separator", "--context-separator", "XX", "needle", path);
        (int pinnedReenabledExitCode, byte[] pinnedReenabledOutput, string pinnedReenabledError) = RunPinnedRipgrep("-H", "-n", "-C1", "--no-context-separator", "--context-separator", "XX", "needle", path);

        Assert.Equal(pinnedEmptyExitCode, emptyExitCode);
        Assert.Equal(pinnedEmptyOutput, emptyOutput);
        Assert.Equal(pinnedEmptyError, emptyError);
        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput);
        Assert.Equal(pinnedDisabledError, disabledError);
        Assert.Equal(pinnedReenabledExitCode, reenabledExitCode);
        Assert.Equal(pinnedReenabledOutput, reenabledOutput);
        Assert.Equal(pinnedReenabledError, reenabledError);
    }

    /// <summary>
    /// Verifies passthrough mode prints every searched line while preserving match fields.
    /// </summary>
    [Fact]
    public void PassthruPrintsEveryLine()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "one\nneedle\ntwo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--passthru"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--passthru", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching output works with contextual lines.
    /// </summary>
    [Fact]
    public void OnlyMatchingContextPrintsFullContextLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "  one\n  needle\n  three\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-C1"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", "--column", "-C1", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching context output honors custom match and context field separators.
    /// </summary>
    [Fact]
    public void OnlyMatchingContextUsesCustomFieldSeparators()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "one\nneedle needle\nthree\n");

        (int exitCode, byte[] output, string error) = RunScout(
            "-o",
            "--column",
            "-C1",
            "--field-match-separator",
            "|",
            "--field-context-separator",
            "_",
            "needle",
            path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "-o",
            "--column",
            "-C1",
            "--field-match-separator",
            "|",
            "--field-context-separator",
            "_",
            "needle",
            path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies inverted only-matching context lines use contextual field separators.
    /// </summary>
    [Fact]
    public void InvertOnlyMatchingContextPrintsOriginalMatchesAsContext()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xx\nneedle needle\nyy\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-C1"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-v", "-o", "--column", "-C1", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies inverted only-matching context output honors custom contextual field separators.
    /// </summary>
    [Fact]
    public void InvertOnlyMatchingContextUsesCustomFieldContextSeparator()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xx\nneedle needle\nyy\n");

        (int exitCode, byte[] output, string error) = RunScout(
            "-v",
            "-o",
            "--column",
            "-C1",
            "--field-match-separator",
            "|",
            "--field-context-separator",
            "_",
            "needle",
            path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "-v",
            "-o",
            "--column",
            "-C1",
            "--field-match-separator",
            "|",
            "--field-context-separator",
            "_",
            "needle",
            path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-count limits primary matches while still printing matching context lines.
    /// </summary>
    [Fact]
    public void ContextMaxCountStillPrintsMatchingContextLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "a\nneedle1\nc\nneedle2\ne\nf\ng\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m1"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("-A3"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m1", "-o", "-A3", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies null mode terminates path prefixes with NUL bytes in standard output.
    /// </summary>
    [Fact]
    public void NullTerminatesMatchPathPrefixes()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--null"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--null", "-H", "-n", "--column", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies null mode terminates path prefixes with NUL bytes in contextual output.
    /// </summary>
    [Fact]
    public void NullTerminatesContextPathPrefixes()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "before\nneedle\nafter\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-0"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("-C1"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-0", "-H", "-n", "-C1", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies null mode terminates count path prefixes with NUL bytes.
    /// </summary>
    [Fact]
    public void NullTerminatesCountPathPrefixes()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--null"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--null", "-H", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies null mode terminates file-list paths with NUL bytes.
    /// </summary>
    [Fact]
    public void NullTerminatesFilesWithMatchesPaths()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--null"u8),
            OsString.FromUnixBytes("-l"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--null", "-l", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies null mode terminates files-mode paths with NUL bytes.
    /// </summary>
    [Fact]
    public void NullTerminatesFilesModePaths()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--null"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--files", "--null", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies vimgrep mode prints one full line per match with implied path, line and column fields.
    /// </summary>
    [Fact]
    public void VimgrepPrintsOneLinePerMatch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\nxx needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies vimgrep mode combines only-matching output with byte offsets.
    /// </summary>
    [Fact]
    public void VimgrepOnlyMatchingCombinesWithByteOffsets()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nxx needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "-o", "-b", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "-o", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies vimgrep mode respects disabled line and column fields.
    /// </summary>
    [Fact]
    public void VimgrepRespectsDisabledLineAndColumnFields()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int noLineExitCode, byte[] noLineOutput, string noLineError) = RunScout("--vimgrep", "-N", "needle", path);
        (int pinnedNoLineExitCode, byte[] pinnedNoLineOutput, string pinnedNoLineError) = RunPinnedRipgrep("--vimgrep", "-N", "needle", path);
        (int noColumnExitCode, byte[] noColumnOutput, string noColumnError) = RunScout("--vimgrep", "--no-column", "needle", path);
        (int pinnedNoColumnExitCode, byte[] pinnedNoColumnOutput, string pinnedNoColumnError) = RunPinnedRipgrep("--vimgrep", "--no-column", "needle", path);

        Assert.Equal(pinnedNoLineExitCode, noLineExitCode);
        Assert.Equal(pinnedNoLineOutput, noLineOutput);
        Assert.Equal(pinnedNoLineError, noLineError);
        Assert.Equal(pinnedNoColumnExitCode, noColumnExitCode);
        Assert.Equal(pinnedNoColumnOutput, noColumnOutput);
        Assert.Equal(pinnedNoColumnError, noColumnError);
    }

    /// <summary>
    /// Verifies vimgrep mode respects filename and custom field separator flags.
    /// </summary>
    [Fact]
    public void VimgrepRespectsFilenameAndSeparatorFlags()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\n");
        File.WriteAllText(second, "needle second\n");

        (int exitCode, byte[] output, string error) = RunScout("--sort=path", "--vimgrep", "--no-filename", "--field-match-separator", "|", "needle", first, second);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort=path", "--vimgrep", "--no-filename", "--field-match-separator", "|", "needle", first, second);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies vimgrep mode implies filename prefixes for count summaries.
    /// </summary>
    [Fact]
    public void VimgrepCountPrefixesSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "-c", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-columns omits long matching lines and keeps boundary-length lines.
    /// </summary>
    [Fact]
    public void MaxColumnsOmitsLongMatchingLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "short needle\nthis is a very long needle line\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-M", "12", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-M", "12", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-columns preview writes the leading bytes of long matching lines.
    /// </summary>
    [Fact]
    public void MaxColumnsPreviewPrintsLongLinePrefixes()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "short needle\nthis is a very long needle line\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-M", "12", "--max-columns-preview", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-M", "12", "--max-columns-preview", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-columns applies to context and passthrough lines with context separators.
    /// </summary>
    [Fact]
    public void MaxColumnsAppliesToContextLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "very very long context\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-B1", "-M", "12", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-B1", "-M", "12", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-columns is evaluated after trimming leading whitespace.
    /// </summary>
    [Fact]
    public void MaxColumnsRespectsTrimmedOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "      needle\n      needle long long\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "--trim", "-M", "12", "--max-columns-preview", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "--trim", "-M", "12", "--max-columns-preview", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies only-matching output ignores max-columns.
    /// </summary>
    [Fact]
    public void MaxColumnsDoesNotAffectOnlyMatchingOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-o", "-M", "6", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-o", "-M", "6", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-columns zero disables the line-length limit.
    /// </summary>
    [Fact]
    public void MaxColumnsZeroDisablesLimit()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-M", "0", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-M", "0", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies vimgrep max-columns uses ripgrep's omitted-line summaries.
    /// </summary>
    [Fact]
    public void VimgrepMaxColumnsOmitsLongLinesWithMatchCounts()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "short needle\nneedle needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "-M", "12", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "-M", "12", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies vimgrep max-columns preview includes the remaining-match suffix.
    /// </summary>
    [Fact]
    public void VimgrepMaxColumnsPreviewPrintsRemainingMatchCounts()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "short needle\nneedle needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "-M", "12", "--max-columns-preview", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "-M", "12", "--max-columns-preview", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replace mode rewrites matching lines.
    /// </summary>
    [Fact]
    public void ReplaceRewritesMatchingLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\nxx needle yy\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-r", "X", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-r", "X", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replace mode combines with only-matching fields and adjusted offsets.
    /// </summary>
    [Fact]
    public void ReplaceOnlyMatchingUsesAdjustedOffsets()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "--column", "-b", "-o", "-r", "XX", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "--column", "-b", "-o", "-r", "XX", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replace mode rewrites matching context lines and leaves context-only lines unchanged.
    /// </summary>
    [Fact]
    public void ReplaceAppliesToContextOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\nxx needle yy\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-C1", "-r", "X", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-C1", "-r", "X", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replacement capture expansion follows ripgrep's whole-match and missing-capture behavior.
    /// </summary>
    [Fact]
    public void ReplaceExpandsWholeMatchAndMissingCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        const string replacement = "$$-$0-$1-${0}-${1}";
        (int exitCode, byte[] output, string error) = RunScout("-n", "-r", replacement, "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-r", replacement, "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replacement expansion uses numeric captures from regex groups.
    /// </summary>
    [Fact]
    public void ReplaceExpandsNumericCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabc456\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-r", "$2-$1", "([a-z]+)([0-9]+)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-r", "$2-$1", "([a-z]+)([0-9]+)", path);
        (int onlyExitCode, byte[] onlyOutput, string onlyError) = RunScout("-o", "-r", "${2}-${1}", "([a-z]+)([0-9]+)", path);
        (int pinnedOnlyExitCode, byte[] pinnedOnlyOutput, string pinnedOnlyError) = RunPinnedRipgrep("-o", "-r", "${2}-${1}", "([a-z]+)([0-9]+)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedOnlyExitCode, onlyExitCode);
        Assert.Equal(pinnedOnlyOutput, onlyOutput);
        Assert.Equal(pinnedOnlyError, onlyError);
    }

    /// <summary>
    /// Verifies replacement expansion uses named captures from regex groups.
    /// </summary>
    [Fact]
    public void ReplaceExpandsNamedCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabc456\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-r", "$digits-$word", "(?P<word>[a-z]+)(?P<digits>[0-9]+)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-r", "$digits-$word", "(?P<word>[a-z]+)(?P<digits>[0-9]+)", path);
        (int onlyExitCode, byte[] onlyOutput, string onlyError) = RunScout("-o", "-r", "${digits}-${word}", "(?<word>[a-z]+)(?<digits>[0-9]+)", path);
        (int pinnedOnlyExitCode, byte[] pinnedOnlyOutput, string pinnedOnlyError) = RunPinnedRipgrep("-o", "-r", "${digits}-${word}", "(?<word>[a-z]+)(?<digits>[0-9]+)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedOnlyExitCode, onlyExitCode);
        Assert.Equal(pinnedOnlyOutput, onlyOutput);
        Assert.Equal(pinnedOnlyError, onlyError);
    }

    /// <summary>
    /// Verifies replacement expansion preserves ripgrep capture numbering across alternation branches.
    /// </summary>
    [Fact]
    public void ReplaceExpandsAlternationCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nbar\nabd\nacd\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "-r", "$1:$2:$0", "(foo)|(bar)", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", "(foo)|(bar)", path);
        (int nestedExitCode, byte[] nestedOutput, string nestedError) = RunScout("-o", "-r", "${1}:${2}:${3}:${4}", "(a(b)|a(c))(d)", path);
        (int pinnedNestedExitCode, byte[] pinnedNestedOutput, string pinnedNestedError) = RunPinnedRipgrep("-o", "-r", "${1}:${2}:${3}:${4}", "(a(b)|a(c))(d)", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedNestedExitCode, nestedExitCode);
        Assert.Equal(pinnedNestedOutput, nestedOutput);
        Assert.Equal(pinnedNestedError, nestedError);
    }

    /// <summary>
    /// Verifies replacement expansion collects captures from patterns that use inline regex flags.
    /// </summary>
    [Fact]
    public void ReplaceExpandsInlineFlagCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "FOO\nfoo\nfoobar\nFOObar\nfoo bar\n");

        (int caseExitCode, byte[] caseOutput, string caseError) = RunScout("-n", "-r", "$1", "(?i)(foo)", path);
        (int pinnedCaseExitCode, byte[] pinnedCaseOutput, string pinnedCaseError) = RunPinnedRipgrep("-n", "-r", "$1", "(?i)(foo)", path);
        (int scopedCaseExitCode, byte[] scopedCaseOutput, string scopedCaseError) = RunScout("-n", "-r", "$1:$2", "(?i:(foo))(?-i:(bar))", path);
        (int pinnedScopedCaseExitCode, byte[] pinnedScopedCaseOutput, string pinnedScopedCaseError) = RunPinnedRipgrep("-n", "-r", "$1:$2", "(?i:(foo))(?-i:(bar))", path);
        (int verboseExitCode, byte[] verboseOutput, string verboseError) = RunScout("-n", "-r", "$1", "(?x) ( f # comment\n o o )", path);
        (int pinnedVerboseExitCode, byte[] pinnedVerboseOutput, string pinnedVerboseError) = RunPinnedRipgrep("-n", "-r", "$1", "(?x) ( f # comment\n o o )", path);
        (int scopedVerboseExitCode, byte[] scopedVerboseOutput, string scopedVerboseError) = RunScout("-n", "-r", "$1:$2", "(?x:(f o o))(?-x:(bar))", path);
        (int pinnedScopedVerboseExitCode, byte[] pinnedScopedVerboseOutput, string pinnedScopedVerboseError) = RunPinnedRipgrep("-n", "-r", "$1:$2", "(?x:(f o o))(?-x:(bar))", path);

        Assert.Equal(pinnedCaseExitCode, caseExitCode);
        Assert.Equal(pinnedCaseOutput, caseOutput);
        Assert.Equal(pinnedCaseError, caseError);
        Assert.Equal(pinnedScopedCaseExitCode, scopedCaseExitCode);
        Assert.Equal(pinnedScopedCaseOutput, scopedCaseOutput);
        Assert.Equal(pinnedScopedCaseError, scopedCaseError);
        Assert.Equal(pinnedVerboseExitCode, verboseExitCode);
        Assert.Equal(pinnedVerboseOutput, verboseOutput);
        Assert.Equal(pinnedVerboseError, verboseError);
        Assert.Equal(pinnedScopedVerboseExitCode, scopedVerboseExitCode);
        Assert.Equal(pinnedScopedVerboseOutput, scopedVerboseOutput);
        Assert.Equal(pinnedScopedVerboseError, scopedVerboseError);
    }

    /// <summary>
    /// Verifies replacement expansion backtracks quantified captures to match ripgrep's chosen spans.
    /// </summary>
    [Fact]
    public void ReplaceExpandsBacktrackedQuantifiedCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "aaa\naaab\nab\n");

        (int greedyExitCode, byte[] greedyOutput, string greedyError) = RunScout("-n", "-r", "$1:$2:$0", "(a+)(a)", path);
        (int pinnedGreedyExitCode, byte[] pinnedGreedyOutput, string pinnedGreedyError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", "(a+)(a)", path);
        (int lazyExitCode, byte[] lazyOutput, string lazyError) = RunScout("-n", "-r", "$1:$2:$0", "(a+?)(a)", path);
        (int pinnedLazyExitCode, byte[] pinnedLazyOutput, string pinnedLazyError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", "(a+?)(a)", path);
        (int boundedExitCode, byte[] boundedOutput, string boundedError) = RunScout("-n", "-r", "$1:$2:$0", "(a{1,3})(a)", path);
        (int pinnedBoundedExitCode, byte[] pinnedBoundedOutput, string pinnedBoundedError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", "(a{1,3})(a)", path);

        Assert.Equal(pinnedGreedyExitCode, greedyExitCode);
        Assert.Equal(pinnedGreedyOutput, greedyOutput);
        Assert.Equal(pinnedGreedyError, greedyError);
        Assert.Equal(pinnedLazyExitCode, lazyExitCode);
        Assert.Equal(pinnedLazyOutput, lazyOutput);
        Assert.Equal(pinnedLazyError, lazyError);
        Assert.Equal(pinnedBoundedExitCode, boundedExitCode);
        Assert.Equal(pinnedBoundedOutput, boundedOutput);
        Assert.Equal(pinnedBoundedError, boundedError);
    }

    /// <summary>
    /// Verifies replacement expansion captures shorthand and POSIX regex classes.
    /// </summary>
    [Fact]
    public void ReplaceExpandsRegexClassCaptures()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc123\nabcXYZ\nname_1\nname-1\nspace tab\nspace\ttab\n123\n");

        (int alternationExitCode, byte[] alternationOutput, string alternationError) = RunScout("-n", "-r", "$1:$2:$3:$0", @"([[:alpha:]]+)(\d+)|(\w+)", path);
        (int pinnedAlternationExitCode, byte[] pinnedAlternationOutput, string pinnedAlternationError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$3:$0", @"([[:alpha:]]+)(\d+)|(\w+)", path);
        (int whitespaceExitCode, byte[] whitespaceOutput, string whitespaceError) = RunScout("-n", "-r", "$1:$2:$3:$0", @"(\w+)(\s+)(\w+)", path);
        (int pinnedWhitespaceExitCode, byte[] pinnedWhitespaceOutput, string pinnedWhitespaceError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$3:$0", @"(\w+)(\s+)(\w+)", path);
        (int notDigitExitCode, byte[] notDigitOutput, string notDigitError) = RunScout("-n", "-r", "$1:$2:$0", @"(\D+)(\d+)", path);
        (int pinnedNotDigitExitCode, byte[] pinnedNotDigitOutput, string pinnedNotDigitError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", @"(\D+)(\d+)", path);
        (int posixExitCode, byte[] posixOutput, string posixError) = RunScout("-n", "-r", "$1:$2:$0", "([[:alpha:]]+)([[:digit:]]+)", path);
        (int pinnedPosixExitCode, byte[] pinnedPosixOutput, string pinnedPosixError) = RunPinnedRipgrep("-n", "-r", "$1:$2:$0", "([[:alpha:]]+)([[:digit:]]+)", path);

        Assert.Equal(pinnedAlternationExitCode, alternationExitCode);
        Assert.Equal(pinnedAlternationOutput, alternationOutput);
        Assert.Equal(pinnedAlternationError, alternationError);
        Assert.Equal(pinnedWhitespaceExitCode, whitespaceExitCode);
        Assert.Equal(pinnedWhitespaceOutput, whitespaceOutput);
        Assert.Equal(pinnedWhitespaceError, whitespaceError);
        Assert.Equal(pinnedNotDigitExitCode, notDigitExitCode);
        Assert.Equal(pinnedNotDigitOutput, notDigitOutput);
        Assert.Equal(pinnedNotDigitError, notDigitError);
        Assert.Equal(pinnedPosixExitCode, posixExitCode);
        Assert.Equal(pinnedPosixOutput, posixOutput);
        Assert.Equal(pinnedPosixError, posixError);
    }

    /// <summary>
    /// Verifies replace mode combines with vimgrep fields and adjusted offsets.
    /// </summary>
    [Fact]
    public void ReplaceVimgrepUsesAdjustedColumnsAndOffsets()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--vimgrep", "-b", "-r", "XX", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--vimgrep", "-b", "-r", "XX", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies replace mode uses max-column match-count summaries.
    /// </summary>
    [Fact]
    public void ReplaceMaxColumnsUsesMatchCountSummaries()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int omittedExitCode, byte[] omittedOutput, string omittedError) = RunScout("-n", "-M", "4", "-r", "ABCDE", "needle", path);
        (int pinnedOmittedExitCode, byte[] pinnedOmittedOutput, string pinnedOmittedError) = RunPinnedRipgrep("-n", "-M", "4", "-r", "ABCDE", "needle", path);
        (int previewExitCode, byte[] previewOutput, string previewError) = RunScout("-n", "-M", "4", "--max-columns-preview", "-r", "ABCDE", "needle", path);
        (int pinnedPreviewExitCode, byte[] pinnedPreviewOutput, string pinnedPreviewError) = RunPinnedRipgrep("-n", "-M", "4", "--max-columns-preview", "-r", "ABCDE", "needle", path);

        Assert.Equal(pinnedOmittedExitCode, omittedExitCode);
        Assert.Equal(pinnedOmittedOutput, omittedOutput);
        Assert.Equal(pinnedOmittedError, omittedError);
        Assert.Equal(pinnedPreviewExitCode, previewExitCode);
        Assert.Equal(pinnedPreviewOutput, previewOutput);
        Assert.Equal(pinnedPreviewError, previewError);
    }

    /// <summary>
    /// Verifies color always highlights matches and standard fields.
    /// </summary>
    [Fact]
    public void ColorAlwaysHighlightsStandardOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\nxx needle yy\n");

        (int exitCode, byte[] output, string error) = RunScout("--color=always", "-H", "-n", "--column", "-b", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--color=always", "-H", "-n", "--column", "-b", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies color always highlights only-matching output.
    /// </summary>
    [Fact]
    public void ColorAlwaysHighlightsOnlyMatchingOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--color=always", "-n", "--column", "-b", "-o", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--color=always", "-n", "--column", "-b", "-o", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies color always highlights vimgrep output one match at a time.
    /// </summary>
    [Fact]
    public void ColorAlwaysHighlightsVimgrepOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--color=always", "--vimgrep", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--color=always", "--vimgrep", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies color always highlights replacement output.
    /// </summary>
    [Fact]
    public void ColorAlwaysHighlightsReplacementOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--color=always", "-n", "-r", "X", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--color=always", "-n", "-r", "X", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies color never suppresses ANSI output.
    /// </summary>
    [Fact]
    public void ColorNeverSuppressesAnsiOutput()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--color=never", "-n", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--color=never", "-n", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--colors</c> customizes ANSI color output.
    /// </summary>
    [Fact]
    public void ColorSpecsCustomizeOutputLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "xx needle yy\nneedle needle\n");
        string[][] argumentPrefixes =
        [
            ["--color=always", "--colors", "match:fg:blue"],
            ["--color=always", "--colors", "match:style:nobold"],
            ["--color=always", "--colors", "path:none", "-H"],
            ["--color=always", "--colors", "line:bg:yellow", "-n"],
            ["--color=always", "--colors", "column:fg:yellow", "-n", "--column", "-b"],
            ["--color=always", "--colors", "match:bg:0xff,0x7f,0x00"],
            ["--color=always", "--colors", "match:fg:240"],
            ["--color=always", "--colors", "match:style:underline", "--colors", "match:style:italic"],
            ["--color=always", "--colors", "highlight:bg:yellow"],
        ];

        foreach (string[] argumentPrefix in argumentPrefixes)
        {
            string[] arguments = [.. argumentPrefix, "needle", path];

            (int exitCode, byte[] output, string error) = RunScout(arguments);
            (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(arguments);

            Assert.Equal(pinnedExitCode, exitCode);
            Assert.Equal(pinnedOutput, output);
            Assert.Equal(pinnedError, error);
        }
    }

    /// <summary>
    /// Verifies <c>--hyperlink-format</c> emits OSC-8 hyperlinks around path preludes.
    /// </summary>
    [Fact]
    public void HyperlinkFormatEmitsPathPreludesLikeRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input file.txt");
        File.WriteAllText(path, "xx needle yy\nneedle\n");
        string[][] argumentPrefixes =
        [
            ["--color=always", "--hyperlink-format", "file://{path}", "-H", "-n", "--column"],
            ["--color=always", "--hyperlink-format", "file://{path}:{line}:{column}", "-H", "-n", "--column"],
            ["--color=always", "--hyperlink-format", "file://{path}", "-H", "-c"],
            ["--color=always", "--hyperlink-format", "file://{path}", "-l"],
            ["--color=never", "--hyperlink-format", "file://{path}", "-H", "-n", "--column"],
        ];

        foreach (string[] argumentPrefix in argumentPrefixes)
        {
            string[] arguments = [.. argumentPrefix, "needle", path];

            (int exitCode, byte[] output, string error) = RunScout(arguments);
            (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(arguments);

            Assert.Equal(pinnedExitCode, exitCode);
            Assert.Equal(pinnedOutput, output);
            Assert.Equal(pinnedError, error);
        }
    }

    /// <summary>
    /// Verifies pretty output aliases color, heading and line-number behavior.
    /// </summary>
    [Fact]
    public void PrettyOutputMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("-p", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-p", "needle", path);
        (int overriddenExitCode, byte[] overriddenOutput, string overriddenError) = RunScout("--pretty", "--color=never", "--no-heading", "-N", "needle", path);
        (int pinnedOverriddenExitCode, byte[] pinnedOverriddenOutput, string pinnedOverriddenError) = RunPinnedRipgrep("--pretty", "--color=never", "--no-heading", "-N", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedOverriddenExitCode, overriddenExitCode);
        Assert.Equal(pinnedOverriddenOutput, overriddenOutput);
        Assert.Equal(pinnedOverriddenError, overriddenError);
    }

    /// <summary>
    /// Verifies unrestricted filtering levels match ripgrep for ignored and hidden files.
    /// </summary>
    [Fact]
    public void UnrestrictedFlagsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".ignore"), "ignored.txt\n");
        File.WriteAllText(Path.Combine(root, "ignored.txt"), "needle ignored\n");
        File.WriteAllText(Path.Combine(root, ".hidden"), "needle hidden\n");

        (int oneExitCode, byte[] oneOutput, string oneError) = RunScout("--sort=path", "-u", "needle", root);
        (int pinnedOneExitCode, byte[] pinnedOneOutput, string pinnedOneError) = RunPinnedRipgrep("--sort=path", "-u", "needle", root);
        (int twoExitCode, byte[] twoOutput, string twoError) = RunScout("--sort=path", "-uu", "needle", root);
        (int pinnedTwoExitCode, byte[] pinnedTwoOutput, string pinnedTwoError) = RunPinnedRipgrep("--sort=path", "-uu", "needle", root);

        Assert.Equal(pinnedOneExitCode, oneExitCode);
        Assert.Equal(pinnedOneOutput, oneOutput);
        Assert.Equal(pinnedOneError, oneError);
        Assert.Equal(pinnedTwoExitCode, twoExitCode);
        Assert.Equal(pinnedTwoOutput, twoOutput);
        Assert.Equal(pinnedTwoError, twoError);
    }

    /// <summary>
    /// Verifies only-matching mode prints each match on its own line.
    /// </summary>
    [Fact]
    public void OnlyMatchingPrintsEachMatch()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one needle\nxx needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching mode combines with filename, line, column and byte-offset fields.
    /// </summary>
    [Fact]
    public void OnlyMatchingCombinesWithStandardFields()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\nneedle one needle\nxx needle two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-H"u8),
            OsString.FromUnixBytes("-n"u8),
            OsString.FromUnixBytes("--column"u8),
            OsString.FromUnixBytes("-b"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-H", "-n", "--column", "-b", "-o", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching mode respects word-regexp matching.
    /// </summary>
    [Fact]
    public void OnlyMatchingRespectsWordRegexp()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo foo2 foo\nfoo-bar\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-w"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-w", "-o", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies only-matching mode respects line-regexp matching.
    /// </summary>
    [Fact]
    public void OnlyMatchingRespectsLineRegexp()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "foo\nfoo-bar\nxx foo\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-x"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("foo"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-x", "-o", "foo", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies count mode with only-matching counts matches instead of matching lines.
    /// </summary>
    [Fact]
    public void OnlyMatchingCountCountsMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", "-c", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies inverted matching ignores only-matching output.
    /// </summary>
    [Fact]
    public void OnlyMatchingInvertMatchPrintsInvertedLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", "-v", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies empty-pattern only-matching output and counts match ripgrep.
    /// </summary>
    [Fact]
    public void OnlyMatchingEmptyPatternMatchesEachByte()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "abc\n\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        using MemoryStream countOutput = new();
        using MemoryStream countError = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        var countOutputWriter = new RawByteWriter(countOutput);
        var countErrorWriter = new RawByteWriter(countError);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes(""u8),
            OsString.FromText(path),
        ];
        OsString[] countArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes(""u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        int countExitCode = ScoutApplication.Run(countArguments, countOutputWriter, countErrorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-o", string.Empty, path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("-o", "--count-matches", string.Empty, path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput.ToArray());
        Assert.Equal(pinnedCountError, Utf8(countError.ToArray()));
    }

    /// <summary>
    /// Verifies regexes that can match empty iterate only the ripgrep-reported offsets.
    /// </summary>
    [Fact]
    public void OnlyMatchingRegexEmptyIterationMatchesRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "b\n");

        (int alternationExitCode, byte[] alternationOutput, string alternationError) = RunScout("-o", "b|", path);
        (int pinnedAlternationExitCode, byte[] pinnedAlternationOutput, string pinnedAlternationError) = RunPinnedRipgrep("-o", "b|", path);
        (int emptyRepeatExitCode, byte[] emptyRepeatOutput, string emptyRepeatError) = RunScout("-o", "(?:)+", path);
        (int pinnedEmptyRepeatExitCode, byte[] pinnedEmptyRepeatOutput, string pinnedEmptyRepeatError) = RunPinnedRipgrep("-o", "(?:)+", path);
        (int countExitCode, byte[] countOutput, string countError) = RunScout("--count-matches", "(?:)+", path);
        (int pinnedCountExitCode, byte[] pinnedCountOutput, string pinnedCountError) = RunPinnedRipgrep("--count-matches", "(?:)+", path);

        Assert.Equal(pinnedAlternationExitCode, alternationExitCode);
        Assert.Equal(pinnedAlternationOutput, alternationOutput);
        Assert.Equal(pinnedAlternationError, alternationError);
        Assert.Equal(pinnedEmptyRepeatExitCode, emptyRepeatExitCode);
        Assert.Equal(pinnedEmptyRepeatOutput, emptyRepeatOutput);
        Assert.Equal(pinnedEmptyRepeatError, emptyRepeatError);
        Assert.Equal(pinnedCountExitCode, countExitCode);
        Assert.Equal(pinnedCountOutput, countOutput);
        Assert.Equal(pinnedCountError, countError);
    }

    /// <summary>
    /// Verifies JSON only-matching emits empty regex submatches at ripgrep-compatible offsets.
    /// </summary>
    [Fact]
    public void JsonOnlyMatchingRegexEmptyIterationMatchesRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "b\n");

        (int exitCode, byte[] output, string error) = RunScout("--json", "-o", "-U", "(?:)+", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--json", "-o", "-U", "(?:)+", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(NormalizeJsonTimings(pinnedOutput), NormalizeJsonTimings(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies max-count limits matching lines in standard output.
    /// </summary>
    [Fact]
    public void MaxCountLimitsStandardMatches()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle1\nneedle2\nmiss\nneedle3\nneedle4\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
            OsString.FromUnixBytes("2"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m", "2", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies inline max-count forms match ripgrep parsing.
    /// </summary>
    [Fact]
    public void MaxCountSupportsInlineForms()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle1\nneedle2\nneedle3\n");
        using MemoryStream shortOutput = new();
        using MemoryStream shortError = new();
        using MemoryStream longOutput = new();
        using MemoryStream longError = new();
        var shortOutputWriter = new RawByteWriter(shortOutput);
        var shortErrorWriter = new RawByteWriter(shortError);
        var longOutputWriter = new RawByteWriter(longOutput);
        var longErrorWriter = new RawByteWriter(longError);
        OsString[] shortArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m2"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];
        OsString[] longArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--max-count=2"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int shortExitCode = ScoutApplication.Run(shortArguments, shortOutputWriter, shortErrorWriter);
        int longExitCode = ScoutApplication.Run(longArguments, longOutputWriter, longErrorWriter);
        (int pinnedShortExitCode, byte[] pinnedShortOutput, string pinnedShortError) = RunPinnedRipgrep("-m2", "needle", path);
        (int pinnedLongExitCode, byte[] pinnedLongOutput, string pinnedLongError) = RunPinnedRipgrep("--max-count=2", "needle", path);

        Assert.Equal(pinnedShortExitCode, shortExitCode);
        Assert.Equal(pinnedShortOutput, shortOutput.ToArray());
        Assert.Equal(pinnedShortError, Utf8(shortError.ToArray()));
        Assert.Equal(pinnedLongExitCode, longExitCode);
        Assert.Equal(pinnedLongOutput, longOutput.ToArray());
        Assert.Equal(pinnedLongError, Utf8(longError.ToArray()));
    }

    /// <summary>
    /// Verifies max-count limits count-matches by matching line, not occurrence count.
    /// </summary>
    [Fact]
    public void MaxCountLimitsCountMatchesByMatchingLine()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nneedle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
            OsString.FromUnixBytes("1"u8),
            OsString.FromUnixBytes("--count-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m", "1", "--count-matches", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies max-count with only-matching prints all matches from each retained matching line.
    /// </summary>
    [Fact]
    public void MaxCountOnlyMatchingPrintsAllMatchesInLimitedLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle needle\nneedle\nneedle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
            OsString.FromUnixBytes("1"u8),
            OsString.FromUnixBytes("-o"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m", "1", "-o", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies max-count limits inverted matching lines.
    /// </summary>
    [Fact]
    public void MaxCountInvertMatchLimitsInvertedLines()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nmiss one\nmiss two\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
            OsString.FromUnixBytes("1"u8),
            OsString.FromUnixBytes("-v"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m", "1", "-v", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies max-count zero disables searches, including files-without-match summaries.
    /// </summary>
    [Fact]
    public void MaxCountZeroDisablesSearchSummaries()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\nmiss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
            OsString.FromUnixBytes("0"u8),
            OsString.FromUnixBytes("--files-without-match"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-m", "0", "--files-without-match", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies max-count parser diagnostics match ripgrep.
    /// </summary>
    [Fact]
    public void MaxCountParserDiagnosticsMatchRipgrep()
    {
        using MemoryStream missingOutput = new();
        using MemoryStream missingError = new();
        using MemoryStream invalidOutput = new();
        using MemoryStream invalidError = new();
        var missingOutputWriter = new RawByteWriter(missingOutput);
        var missingErrorWriter = new RawByteWriter(missingError);
        var invalidOutputWriter = new RawByteWriter(invalidOutput);
        var invalidErrorWriter = new RawByteWriter(invalidError);
        OsString[] missingArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-m"u8),
        ];
        OsString[] invalidArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--max-count=x"u8),
            OsString.FromUnixBytes("needle"u8),
        ];

        int missingExitCode = ScoutApplication.Run(missingArguments, missingOutputWriter, missingErrorWriter);
        int invalidExitCode = ScoutApplication.Run(invalidArguments, invalidOutputWriter, invalidErrorWriter);
        (int pinnedMissingExitCode, byte[] pinnedMissingOutput, string pinnedMissingError) = RunPinnedRipgrep("-m");
        (int pinnedInvalidExitCode, byte[] pinnedInvalidOutput, string pinnedInvalidError) = RunPinnedRipgrep("--max-count=x", "needle");

        Assert.Equal(pinnedMissingExitCode, missingExitCode);
        Assert.Equal(pinnedMissingOutput, missingOutput.ToArray());
        Assert.Equal(pinnedMissingError, Utf8(missingError.ToArray()));
        Assert.Equal(pinnedInvalidExitCode, invalidExitCode);
        Assert.Equal(pinnedInvalidOutput, invalidOutput.ToArray());
        Assert.Equal(pinnedInvalidError, Utf8(invalidError.ToArray()));
    }

    /// <summary>
    /// Verifies count mode prefixes file paths when multiple paths are searched.
    /// </summary>
    [Fact]
    public void LiteralSearchCountPrefixesMultipleFiles()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\nneedle again\n");
        File.WriteAllText(second, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-c"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{first}:2\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files-with-matches mode prints matching paths.
    /// </summary>
    [Fact]
    public void LiteralSearchFilesWithMatchesPrintsMatchingPaths()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\n");
        File.WriteAllText(second, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files-with-matches"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{first}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files-with-matches mode returns no-match when no paths match.
    /// </summary>
    [Fact]
    public void LiteralSearchFilesWithMatchesReturnsNoMatchForUnmatchedPaths()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-l"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files-without-match mode prints paths with no matches.
    /// </summary>
    [Fact]
    public void LiteralSearchFilesWithoutMatchPrintsUnmatchedPaths()
    {
        string root = CreateTempDirectory();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        File.WriteAllText(first, "needle first\n");
        File.WriteAllText(second, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files-without-match"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(first),
            OsString.FromText(second),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{second}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files-without-match mode returns no-match when every path matches.
    /// </summary>
    [Fact]
    public void LiteralSearchFilesWithoutMatchReturnsNoMatchForMatchedPaths()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files-without-match"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files mode lists searchable files without requiring a pattern.
    /// </summary>
    [Fact]
    public void FilesModeListsSearchableFiles()
    {
        string root = CreateTempDirectory();
        string keep = Path.Combine(root, "keep.txt");
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(keep, "keep\n");
        File.WriteAllText(Path.Combine(root, "drop.log"), "drop\n");
        File.WriteAllText(Path.Combine(root, ".hidden"), "hidden\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{keep}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies explicit multi-threaded files mode lists the same searchable files as ripgrep.
    /// </summary>
    [Fact]
    public void FilesModeExplicitThreadsListsRipgrepFileSet()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, "left", "one.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, "left", "drop.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "right", "two.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, ".hidden"), string.Empty);

        (int exitCode, byte[] output, string error) = RunScout("--files", "--threads", "2", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--files", "--threads", "2", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(SortedUtf8Lines(pinnedOutput), SortedUtf8Lines(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies default files mode lists the same searchable file set as ripgrep.
    /// </summary>
    [Fact]
    public void FilesModeDefaultThreadsListsRipgrepFileSet()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(root, "left", "one.txt"), string.Empty);
        File.WriteAllText(Path.Combine(root, "left", "drop.log"), string.Empty);
        File.WriteAllText(Path.Combine(root, "right", "two.txt"), string.Empty);

        (int exitCode, byte[] output, string error) = RunScout("--files", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--files", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(SortedUtf8Lines(pinnedOutput), SortedUtf8Lines(output));
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies quiet files mode suppresses file output while preserving found status.
    /// </summary>
    [Fact]
    public void QuietFilesModeSuppressesFileOutput()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.txt"), "keep\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-q"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "--files", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies files mode lists direct file path arguments.
    /// </summary>
    [Fact]
    public void FilesModeListsDirectFilePath()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "keep\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{path}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files mode prints the stdin label for standard input.
    /// </summary>
    [Fact]
    public void FilesModeListsStandardInputLabel()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("-"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal("<stdin>\n"u8.ToArray(), output.ToArray());
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies quiet files-without-match uses unmatched files for success status.
    /// </summary>
    [Fact]
    public void QuietFilesWithoutMatchUsesUnmatchedStatus()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-q"u8),
            OsString.FromUnixBytes("--files-without-match"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(path),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-q", "--files-without-match", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies directory searches recurse through the ignore-aware walker and print path prefixes.
    /// </summary>
    [Fact]
    public void LiteralSearchRecursesDirectories()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "child"));
        File.WriteAllText(Path.Combine(root, "child", "keep.txt"), "needle child\n");
        File.WriteAllText(Path.Combine(root, "miss.txt"), "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{Path.Combine(root, "child", "keep.txt")}:needle child\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies directory searches print line numbers after path prefixes.
    /// </summary>
    [Fact]
    public void LiteralSearchPrintsDirectoryLineNumbersAfterPathPrefixes()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "child"));
        File.WriteAllText(Path.Combine(root, "child", "keep.txt"), "miss\nneedle child\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--line-number"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{Path.Combine(root, "child", "keep.txt")}:2:needle child\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies hidden search includes hidden files while respecting ignore files.
    /// </summary>
    [Fact]
    public void LiteralSearchHiddenFlagIncludesHiddenFiles()
    {
        string root = CreateTempDirectory();
        string hidden = Path.Combine(root, ".hidden");
        File.WriteAllText(hidden, "needle hidden\n");
        File.WriteAllText(Path.Combine(root, "visible.txt"), "miss\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--hidden"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{hidden}:needle hidden\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies no-ignore search includes ignored files without including hidden files.
    /// </summary>
    [Fact]
    public void LiteralSearchNoIgnoreIncludesIgnoredFilesButNotHiddenFiles()
    {
        string root = CreateTempDirectory();
        string ignored = Path.Combine(root, "ignored.log");
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(ignored, "needle ignored\n");
        File.WriteAllText(Path.Combine(root, ".hidden"), "needle hidden\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--no-ignore"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{ignored}:needle ignored\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies max-depth limits recursive directory searches.
    /// </summary>
    [Fact]
    public void LiteralSearchMaxDepthLimitsRecursion()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "child", "nested"));
        File.WriteAllText(Path.Combine(root, "child", "direct.txt"), "needle direct\n");
        File.WriteAllText(Path.Combine(root, "child", "nested", "deep.txt"), "needle deep\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--max-depth"u8),
            OsString.FromUnixBytes("2"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--max-depth", "2", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies follow mode recurses through directory symbolic links.
    /// </summary>
    [Fact]
    public void LiteralSearchFollowTraversesDirectorySymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTempDirectory();
        string target = CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "linked.txt"), "needle linked\n");
        if (!TryCreateDirectorySymlink(target, Path.Combine(root, "link")))
        {
            return;
        }

        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-L"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-L", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies glob whitelist mode filters searched files.
    /// </summary>
    [Fact]
    public void LiteralSearchGlobWhitelistFiltersFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.cs"), "needle keep\n");
        File.WriteAllText(Path.Combine(root, "skip.txt"), "needle skip\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-g"u8),
            OsString.FromUnixBytes("*.cs"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-g", "*.cs", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies negated globs exclude matching files without enabling whitelist mode.
    /// </summary>
    [Fact]
    public void LiteralSearchNegatedGlobExcludesFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.txt"), "needle keep\n");
        File.WriteAllText(Path.Combine(root, "skip.log"), "needle skip\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--glob=!*.log"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--glob=!*.log", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies glob whitelists override hidden filtering.
    /// </summary>
    [Fact]
    public void LiteralSearchGlobWhitelistOverridesHiddenFilter()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, ".hidden"), "needle hidden\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-g.hidden"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-g.hidden", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies case-insensitive glob overrides match ripgrep.
    /// </summary>
    [Fact]
    public void CaseInsensitiveGlobOverridesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "app.cs"), "needle\n");

        AssertFilesMatchPinned("--files", "--sort=path", "--iglob", "*.CS", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--iglob", "*.CS", "--no-glob-case-insensitive", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--glob-case-insensitive", "-g", "*.CS", root);
        AssertFilesMatchPinned(
            "--files",
            "--sort=path",
            "--glob-case-insensitive",
            "-g",
            "*.CS",
            "--no-glob-case-insensitive",
            root);
    }

    /// <summary>
    /// Verifies path sorting orders directory search output like ripgrep.
    /// </summary>
    [Fact]
    public void LiteralSearchSortPathOrdersDirectoryMatches()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "b"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        File.WriteAllText(Path.Combine(root, "b", "match.txt"), "needle b\n");
        File.WriteAllText(Path.Combine(root, "a", "match.txt"), "needle a\n");
        File.WriteAllText(Path.Combine(root, "z.txt"), "needle z\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--sort"u8),
            OsString.FromUnixBytes("path"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort", "path", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies reverse path sorting orders directory search output like ripgrep.
    /// </summary>
    [Fact]
    public void LiteralSearchSortReversePathOrdersDirectoryMatches()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "b"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        File.WriteAllText(Path.Combine(root, "b", "match.txt"), "needle b\n");
        File.WriteAllText(Path.Combine(root, "a", "match.txt"), "needle a\n");
        File.WriteAllText(Path.Combine(root, "z.txt"), "needle z\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--sortr=path"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sortr=path", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies modified-time sorting orders directory search output like ripgrep.
    /// </summary>
    [Fact]
    public void LiteralSearchSortModifiedOrdersDirectoryMatches()
    {
        string root = CreateTempDirectory();
        string newest = Path.Combine(root, "newest.txt");
        string oldest = Path.Combine(root, "oldest.txt");
        File.WriteAllText(newest, "needle newest\n");
        File.WriteAllText(oldest, "needle oldest\n");
        File.SetLastWriteTimeUtc(newest, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(oldest, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--sort=modified"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--sort=modified", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies type-list output matches pinned ripgrep's default type table.
    /// </summary>
    [Fact]
    public void TypeListWritesPinnedTypeDefinitions()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--type-list"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--type-list");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies type-add and type-clear affect type-list output like ripgrep.
    /// </summary>
    [Fact]
    public void TypeListHonorsTypeAddAndClear()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--type-clear=foo"u8),
            OsString.FromUnixBytes("--type-add"u8),
            OsString.FromUnixBytes("foo:include:cs,txt"u8),
            OsString.FromUnixBytes("--type-add=foo:*.foo"u8),
            OsString.FromUnixBytes("--type-list"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--type-clear=foo", "--type-add", "foo:include:cs,txt", "--type-add=foo:*.foo", "--type-list");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies type selection filters directory searches.
    /// </summary>
    [Fact]
    public void LiteralSearchTypeSelectionFiltersFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.cs"), "needle cs\n");
        File.WriteAllText(Path.Combine(root, "drop.txt"), "needle txt\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-tcs"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-tcs", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies type negation excludes matching file types.
    /// </summary>
    [Fact]
    public void LiteralSearchTypeNotExcludesFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.cs"), "needle cs\n");
        File.WriteAllText(Path.Combine(root, "drop.txt"), "needle txt\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("-Ttxt"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-Ttxt", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies custom file type definitions can be selected.
    /// </summary>
    [Fact]
    public void LiteralSearchTypeAddDefinesSelectableType()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.foo"), "needle foo\n");
        File.WriteAllText(Path.Combine(root, "drop.txt"), "needle txt\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--type-add"u8),
            OsString.FromUnixBytes("foo:*.foo"u8),
            OsString.FromUnixBytes("-tfoo"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--type-add", "foo:*.foo", "-tfoo", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies type filtering applies to files mode.
    /// </summary>
    [Fact]
    public void FilesModeTypeSelectionFiltersFiles()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.cs"), string.Empty);
        File.WriteAllText(Path.Combine(root, "drop.txt"), string.Empty);
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--type=cs"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--files", "--type=cs", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies unknown type diagnostics match ripgrep.
    /// </summary>
    [Fact]
    public void UnknownTypeDiagnosticMatchesRipgrep()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "keep.cs"), "needle\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--type=bogus"u8),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--type=bogus", "needle", root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies invalid type-add diagnostics match ripgrep.
    /// </summary>
    [Fact]
    public void InvalidTypeAddDiagnosticMatchesRipgrep()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--type-add"u8),
            OsString.FromUnixBytes("bad"u8),
            OsString.FromUnixBytes("--type-list"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--type-add", "bad", "--type-list");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies files mode honors hidden traversal.
    /// </summary>
    [Fact]
    public void FilesModeHiddenFlagIncludesHiddenFiles()
    {
        string root = CreateTempDirectory();
        string hidden = Path.Combine(root, ".hidden");
        File.WriteAllText(hidden, "hidden\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--hidden"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{hidden}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies files mode honors disabled ignore files.
    /// </summary>
    [Fact]
    public void FilesModeNoIgnoreIncludesIgnoredFiles()
    {
        string root = CreateTempDirectory();
        string ignored = Path.Combine(root, "ignored.log");
        File.WriteAllText(Path.Combine(root, ".ignore"), "*.log\n");
        File.WriteAllText(ignored, "ignored\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--no-ignore"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{ignored}\n", Utf8(output.ToArray()));
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies explicit ignore files match ripgrep when searching directories.
    /// </summary>
    [Fact]
    public void ExplicitIgnoreFileSearchMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string ignoreRoot = CreateTempDirectory();
        string keep = Path.Combine(root, "keep.txt");
        string ignored = Path.Combine(root, "ignored.log");
        string ignoreFile = Path.Combine(ignoreRoot, "rules.ignore");
        File.WriteAllText(keep, "needle\n");
        File.WriteAllText(ignored, "needle\n");
        File.WriteAllText(ignoreFile, "*.log\n");
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--sort=path"u8),
            OsString.FromUnixBytes("--ignore-file"u8),
            OsString.FromText(ignoreFile),
            OsString.FromUnixBytes("needle"u8),
            OsString.FromText(root),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "--sort=path",
            "--ignore-file",
            ignoreFile,
            "needle",
            root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output.ToArray());
        Assert.Equal(pinnedError, Utf8(error.ToArray()));
    }

    /// <summary>
    /// Verifies explicit ignore-file toggles match ripgrep in files mode.
    /// </summary>
    [Fact]
    public void IgnoreFilesToggleMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string ignoreRoot = CreateTempDirectory();
        string keep = Path.Combine(root, "keep.txt");
        string ignored = Path.Combine(root, "ignored.log");
        string ignoreFile = Path.Combine(ignoreRoot, "rules.ignore");
        File.WriteAllText(keep, "needle\n");
        File.WriteAllText(ignored, "needle\n");
        File.WriteAllText(ignoreFile, "*.log\n");
        using MemoryStream disabledOutput = new();
        using MemoryStream disabledError = new();
        using MemoryStream enabledOutput = new();
        using MemoryStream enabledError = new();
        var disabledOutputWriter = new RawByteWriter(disabledOutput);
        var disabledErrorWriter = new RawByteWriter(disabledError);
        var enabledOutputWriter = new RawByteWriter(enabledOutput);
        var enabledErrorWriter = new RawByteWriter(enabledError);
        OsString[] disabledArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--sort=path"u8),
            OsString.FromUnixBytes("--ignore-file"u8),
            OsString.FromText(ignoreFile),
            OsString.FromUnixBytes("--no-ignore-files"u8),
            OsString.FromText(root),
        ];
        OsString[] enabledArguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--files"u8),
            OsString.FromUnixBytes("--sort=path"u8),
            OsString.FromUnixBytes("--ignore-file"u8),
            OsString.FromText(ignoreFile),
            OsString.FromUnixBytes("--no-ignore-files"u8),
            OsString.FromUnixBytes("--ignore-files"u8),
            OsString.FromText(root),
        ];

        int disabledExitCode = ScoutApplication.Run(disabledArguments, disabledOutputWriter, disabledErrorWriter);
        int enabledExitCode = ScoutApplication.Run(enabledArguments, enabledOutputWriter, enabledErrorWriter);
        (int pinnedDisabledExitCode, byte[] pinnedDisabledOutput, string pinnedDisabledError) = RunPinnedRipgrep(
            "--files",
            "--sort=path",
            "--ignore-file",
            ignoreFile,
            "--no-ignore-files",
            root);
        (int pinnedEnabledExitCode, byte[] pinnedEnabledOutput, string pinnedEnabledError) = RunPinnedRipgrep(
            "--files",
            "--sort=path",
            "--ignore-file",
            ignoreFile,
            "--no-ignore-files",
            "--ignore-files",
            root);

        Assert.Equal(pinnedDisabledExitCode, disabledExitCode);
        Assert.Equal(pinnedDisabledOutput, disabledOutput.ToArray());
        Assert.Equal(pinnedDisabledError, Utf8(disabledError.ToArray()));
        Assert.Equal(pinnedEnabledExitCode, enabledExitCode);
        Assert.Equal(pinnedEnabledOutput, enabledOutput.ToArray());
        Assert.Equal(pinnedEnabledError, Utf8(enabledError.ToArray()));
    }

    /// <summary>
    /// Verifies standard ignore source toggles match ripgrep in files mode.
    /// </summary>
    [Fact]
    public void IgnoreSourceTogglesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git", "info"));
        File.WriteAllText(Path.Combine(root, ".ignore"), "dot.log\n");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "vcs.log\n");
        File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), "exclude.log\n");
        File.WriteAllText(Path.Combine(root, "dot.log"), "needle\n");
        File.WriteAllText(Path.Combine(root, "vcs.log"), "needle\n");
        File.WriteAllText(Path.Combine(root, "exclude.log"), "needle\n");
        File.WriteAllText(Path.Combine(root, "keep.txt"), "needle\n");

        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-dot", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-vcs", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-exclude", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-global", "--ignore-global", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-messages", "--ignore-messages", root);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore", "--ignore-dot", root);
    }

    /// <summary>
    /// Verifies parent-ignore, repository-gating and case-insensitive ignore flags match ripgrep.
    /// </summary>
    [Fact]
    public void IgnoreTraversalModifiersMatchPinnedRipgrep()
    {
        string parent = CreateTempDirectory();
        string child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(parent, ".ignore"), "parent.log\n");
        File.WriteAllText(Path.Combine(child, "parent.log"), "needle\n");
        File.WriteAllText(Path.Combine(child, "keep.txt"), "needle\n");
        AssertFilesMatchPinned("--files", "--sort=path", child);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-parent", child);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-ignore-parent", "--ignore-parent", child);

        string requireRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(requireRoot, ".gitignore"), "vcs.log\n");
        File.WriteAllText(Path.Combine(requireRoot, "vcs.log"), "needle\n");
        File.WriteAllText(Path.Combine(requireRoot, "keep.txt"), "needle\n");
        AssertFilesMatchPinned("--files", "--sort=path", requireRoot);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-require-git", requireRoot);
        AssertFilesMatchPinned("--files", "--sort=path", "--no-require-git", "--require-git", requireRoot);

        string caseRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(caseRoot, ".ignore"), "*.log\n");
        File.WriteAllText(Path.Combine(caseRoot, "trace.LOG"), "needle\n");
        File.WriteAllText(Path.Combine(caseRoot, "keep.txt"), "needle\n");
        AssertFilesMatchPinned("--files", "--sort=path", caseRoot);
        AssertFilesMatchPinned("--files", "--sort=path", "--ignore-file-case-insensitive", caseRoot);
        AssertFilesMatchPinned(
            "--files",
            "--sort=path",
            "--ignore-file-case-insensitive",
            "--no-ignore-file-case-insensitive",
            caseRoot);
    }

    /// <summary>
    /// Verifies max-filesize filtering matches ripgrep in files and search modes.
    /// </summary>
    [Fact]
    public void MaxFileSizeMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string small = Path.Combine(root, "small.txt");
        string large = Path.Combine(root, "large.txt");
        File.WriteAllText(small, "abcd");
        File.WriteAllText(large, "abcde");

        AssertFilesMatchPinned("--files", "--sort=path", "--max-filesize=4", root);

        (int exitCode, byte[] output, string error) = RunScout("--sort=path", "--max-filesize=4", "abc", root);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(
            "--sort=path",
            "--max-filesize=4",
            "abc",
            root);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies explicit regexp flags treat positionals as paths.
    /// </summary>
    [Fact]
    public void ExplicitRegexpTreatsPositionalsAsPaths()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "alpha\nneedle\n");

        (int exitCode, byte[] output, string error) = RunScout("-e", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-e", "needle", path);
        (int inlineExitCode, byte[] inlineOutput, string inlineError) = RunScout("--regexp=needle", path);
        (int pinnedInlineExitCode, byte[] pinnedInlineOutput, string pinnedInlineError) = RunPinnedRipgrep("--regexp=needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedInlineExitCode, inlineExitCode);
        Assert.Equal(pinnedInlineOutput, inlineOutput);
        Assert.Equal(pinnedInlineError, inlineError);
    }

    /// <summary>
    /// Verifies multiple explicit regexp flags use ordered leftmost matching.
    /// </summary>
    [Fact]
    public void MultipleExplicitRegexpsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "ababa\nalpha beta alpha\n");

        (int exitCode, byte[] output, string error) = RunScout("-n", "--column", "-o", "-e", "beta", "-e", "alpha", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-n", "--column", "-o", "-e", "beta", "-e", "alpha", path);
        (int leftmostExitCode, byte[] leftmostOutput, string leftmostError) = RunScout("-o", "-e", "ab", "-e", "a", path);
        (int pinnedLeftmostExitCode, byte[] pinnedLeftmostOutput, string pinnedLeftmostError) = RunPinnedRipgrep("-o", "-e", "ab", "-e", "a", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedLeftmostExitCode, leftmostExitCode);
        Assert.Equal(pinnedLeftmostOutput, leftmostOutput);
        Assert.Equal(pinnedLeftmostError, leftmostError);
    }

    /// <summary>
    /// Verifies explicit regexp parser diagnostics match ripgrep at the application boundary.
    /// </summary>
    [Fact]
    public void ExplicitRegexpMissingValueDiagnosticMatchesRipgrep()
    {
        (int exitCode, byte[] output, string error) = RunScout("-e");
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-e");
        (int longExitCode, byte[] longOutput, string longError) = RunScout("--regexp");
        (int pinnedLongExitCode, byte[] pinnedLongOutput, string pinnedLongError) = RunPinnedRipgrep("--regexp");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedLongExitCode, longExitCode);
        Assert.Equal(pinnedLongOutput, longOutput);
        Assert.Equal(pinnedLongError, longError);
    }

    /// <summary>
    /// Verifies pattern files provide search patterns.
    /// </summary>
    [Fact]
    public void PatternFileSearchMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string patterns = Path.Combine(root, "patterns.txt");
        string crlfPatterns = Path.Combine(root, "crlf-patterns.txt");
        File.WriteAllText(path, "alpha\nbeta\ngamma\n");
        File.WriteAllText(patterns, "alpha\nbeta\n");
        File.WriteAllText(crlfPatterns, "alpha\r\nbeta\r\n");

        (int exitCode, byte[] output, string error) = RunScout("-f", patterns, path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-f", patterns, path);
        (int inlineExitCode, byte[] inlineOutput, string inlineError) = RunScout("--file=" + crlfPatterns, path);
        (int pinnedInlineExitCode, byte[] pinnedInlineOutput, string pinnedInlineError) = RunPinnedRipgrep("--file=" + crlfPatterns, path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedInlineExitCode, inlineExitCode);
        Assert.Equal(pinnedInlineOutput, inlineOutput);
        Assert.Equal(pinnedInlineError, inlineError);
    }

    /// <summary>
    /// Verifies empty and blank pattern files match ripgrep.
    /// </summary>
    [Fact]
    public void EmptyAndBlankPatternFilesMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string empty = Path.Combine(root, "empty.txt");
        string blank = Path.Combine(root, "blank.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        File.WriteAllText(empty, string.Empty);
        File.WriteAllText(blank, "alpha\n\n");

        (int emptyExitCode, byte[] emptyOutput, string emptyError) = RunScout("-f", empty, path);
        (int pinnedEmptyExitCode, byte[] pinnedEmptyOutput, string pinnedEmptyError) = RunPinnedRipgrep("-f", empty, path);
        (int blankExitCode, byte[] blankOutput, string blankError) = RunScout("-f", blank, path);
        (int pinnedBlankExitCode, byte[] pinnedBlankOutput, string pinnedBlankError) = RunPinnedRipgrep("-f", blank, path);

        Assert.Equal(pinnedEmptyExitCode, emptyExitCode);
        Assert.Equal(pinnedEmptyOutput, emptyOutput);
        Assert.Equal(pinnedEmptyError, emptyError);
        Assert.Equal(pinnedBlankExitCode, blankExitCode);
        Assert.Equal(pinnedBlankOutput, blankOutput);
        Assert.Equal(pinnedBlankError, blankError);
    }

    /// <summary>
    /// Verifies pattern-file and inline regexp ordering match ripgrep.
    /// </summary>
    [Fact]
    public void PatternFileOrderMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string patternFile = Path.Combine(root, "patterns.txt");
        File.WriteAllText(path, "ab\n");
        File.WriteAllText(patternFile, "a\n");

        (int fileFirstExitCode, byte[] fileFirstOutput, string fileFirstError) = RunScout("-o", "-f", patternFile, "-e", "ab", path);
        (int pinnedFileFirstExitCode, byte[] pinnedFileFirstOutput, string pinnedFileFirstError) = RunPinnedRipgrep("-o", "-f", patternFile, "-e", "ab", path);
        (int regexpFirstExitCode, byte[] regexpFirstOutput, string regexpFirstError) = RunScout("-o", "-e", "ab", "-f", patternFile, path);
        (int pinnedRegexpFirstExitCode, byte[] pinnedRegexpFirstOutput, string pinnedRegexpFirstError) = RunPinnedRipgrep("-o", "-e", "ab", "-f", patternFile, path);

        Assert.Equal(pinnedFileFirstExitCode, fileFirstExitCode);
        Assert.Equal(pinnedFileFirstOutput, fileFirstOutput);
        Assert.Equal(pinnedFileFirstError, fileFirstError);
        Assert.Equal(pinnedRegexpFirstExitCode, regexpFirstExitCode);
        Assert.Equal(pinnedRegexpFirstOutput, regexpFirstOutput);
        Assert.Equal(pinnedRegexpFirstError, regexpFirstError);
    }

    /// <summary>
    /// Verifies pattern-file diagnostics match ripgrep.
    /// </summary>
    [Fact]
    public void PatternFileDiagnosticsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string missing = Path.Combine(root, "missing.txt");
        string invalid = Path.Combine(root, "invalid.txt");
        File.WriteAllText(path, "alpha\n");
        File.WriteAllBytes(invalid, [0xFF, (byte)'\n']);

        (int missingExitCode, byte[] missingOutput, string missingError) = RunScout("-f", missing, path);
        (int pinnedMissingExitCode, byte[] pinnedMissingOutput, string pinnedMissingError) = RunPinnedRipgrep("-f", missing, path);
        (int invalidExitCode, byte[] invalidOutput, string invalidError) = RunScout("-f", invalid, path);
        (int pinnedInvalidExitCode, byte[] pinnedInvalidOutput, string pinnedInvalidError) = RunPinnedRipgrep("-f", invalid, path);

        Assert.Equal(pinnedMissingExitCode, missingExitCode);
        Assert.Equal(pinnedMissingOutput, missingOutput);
        Assert.Equal(pinnedMissingError, missingError);
        Assert.Equal(pinnedInvalidExitCode, invalidExitCode);
        Assert.Equal(pinnedInvalidOutput, invalidOutput);
        Assert.Equal(pinnedInvalidError, invalidError);
    }

    /// <summary>
    /// Verifies explicit pattern-file parser diagnostics match ripgrep at the application boundary.
    /// </summary>
    [Fact]
    public void PatternFileMissingValueDiagnosticMatchesRipgrep()
    {
        (int exitCode, byte[] output, string error) = RunScout("-f");
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("-f");
        (int longExitCode, byte[] longOutput, string longError) = RunScout("--file");
        (int pinnedLongExitCode, byte[] pinnedLongOutput, string pinnedLongError) = RunPinnedRipgrep("--file");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedLongExitCode, longExitCode);
        Assert.Equal(pinnedLongOutput, longOutput);
        Assert.Equal(pinnedLongError, longError);
    }

    /// <summary>
    /// Verifies RIPGREP_CONFIG_PATH arguments are applied before command-line arguments.
    /// </summary>
    [Fact]
    public void ConfigPathArgumentsMatchPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(path, "alpha\nneedle\n");
        File.WriteAllText(config, "-n\nneedle\n" + path + "\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies command-line arguments override earlier config-file arguments.
    /// </summary>
    [Fact]
    public void ConfigPathPrecedenceMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(path, "Needle\nneedle\n");
        File.WriteAllText(config, "--ignore-case\nneedle\n" + path + "\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config, "--case-sensitive");
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config, "--case-sensitive");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies config comments, blank lines and argument trimming match ripgrep.
    /// </summary>
    [Fact]
    public void ConfigPathLineParsingMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(path, "alpha\nneedle\n");
        File.WriteAllText(config, "  -n  \n  # comment\n\nneedle\n" + path + "\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--no-config</c> disables config loading.
    /// </summary>
    [Fact]
    public void NoConfigSkipsConfigPath()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(path, "needle\n");
        File.WriteAllText(config, "--badflag\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config, "--no-config", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config, "--no-config", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies <c>--generate</c> outputs byte-identical man and completion artifacts.
    /// </summary>
    [Fact]
    public void GenerateOutputsMatchPinnedRipgrep()
    {
        AssertGenerateMatchesPinned("man");
        AssertGenerateMatchesPinned("complete-bash");
        AssertGenerateMatchesPinned("complete-zsh");
        AssertGenerateMatchesPinned("complete-fish");
        AssertGenerateMatchesPinned("complete-powershell");
    }

    /// <summary>
    /// Verifies config files can select <c>--generate</c> like ripgrep.
    /// </summary>
    [Fact]
    public void GenerateFromConfigMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(config, "--generate\ncomplete-fish\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies special command-line modes skip config loading.
    /// </summary>
    [Fact]
    public void SpecialModesSkipConfigPath()
    {
        string root = CreateTempDirectory();
        string config = Path.Combine(root, "rg.conf");
        File.WriteAllText(config, "--badflag\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config, "-V");
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config, "-V");

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies unreadable config path diagnostics are non-fatal.
    /// </summary>
    [Fact]
    public void MissingConfigPathDiagnosticMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        string config = Path.Combine(root, "missing.conf");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScoutWithConfig(config, "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrepWithConfig(config, "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    private static void AssertFilesMatchPinned(params string[] arguments)
    {
        (int exitCode, byte[] output, string error) = RunScout(arguments);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(arguments);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    private static void AssertGenerateMatchesPinned(string kind)
    {
        (int exitCode, byte[] output, string error) = RunScout("--generate", kind);
        (int inlineExitCode, byte[] inlineOutput, string inlineError) = RunScout("--generate=" + kind);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--generate", kind);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
        Assert.Equal(pinnedExitCode, inlineExitCode);
        Assert.Equal(pinnedOutput, inlineOutput);
        Assert.Equal(pinnedError, inlineError);
    }

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
    /// Verifies debug logging matches the pinned ripgrep diagnostics for a single file.
    /// </summary>
    [Fact]
    public void DebugLoggingMatchesPinnedRipgrepForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--debug", "--no-config", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--debug", "--no-config", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
    }

    /// <summary>
    /// Verifies trace logging matches the pinned ripgrep diagnostics for a single file.
    /// </summary>
    [Fact]
    public void TraceLoggingMatchesPinnedRipgrepForSingleFile()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "input.txt");
        File.WriteAllText(path, "needle\n");

        (int exitCode, byte[] output, string error) = RunScout("--trace", "--no-config", "needle", path);
        (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep("--trace", "--no-config", "needle", path);

        Assert.Equal(pinnedExitCode, exitCode);
        Assert.Equal(pinnedOutput, output);
        Assert.Equal(pinnedError, error);
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

        int exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter, configPath: null);
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

        int exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter, configPath);
        return (exitCode, output.ToArray(), Utf8(error.ToArray()));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
        ProcessStartInfo startInfo = new(program)
        {
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        process.StandardInput.BaseStream.Write(contents);
        process.StandardInput.Close();
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        File.WriteAllBytes(path, output.ToArray());
    }

    private static string CreatePreprocessorScript(string root)
    {
        string path = Path.Combine(root, "preprocessor.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat >/dev/null\nprintf 'needle from preprocessor\\n'\n");

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
        ProcessStartInfo startInfo = new("/Users/brandon/src/ripgrep/target/release-lto/rg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Remove("RIPGREP_CONFIG_PATH");
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

        return (process.ExitCode, output.ToArray(), error);
    }

    private static (int ExitCode, byte[] Output, string Error) RunPinnedRipgrepWithConfig(string configPath, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("/Users/brandon/src/ripgrep/target/release-lto/rg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
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

        return (process.ExitCode, output.ToArray(), error);
    }
}
