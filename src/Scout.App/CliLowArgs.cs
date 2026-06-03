using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Stores the low-level command-line arguments recognized before high-level conversion.
/// </summary>
public sealed class CliLowArgs
{
    private static readonly byte[] DefaultFieldMatchSeparator = [(byte)':'];
    private static readonly byte[] DefaultFieldContextSeparator = [(byte)'-'];
    private static readonly byte[] DefaultContextSeparator = [(byte)'-', (byte)'-'];

    private readonly List<OsString> positional = [];
    private readonly List<OsString> patterns = [];
    private readonly List<CliPatternSource> patternSources = [];
    private readonly List<CliGlobPattern> globPatterns = [];
    private readonly List<string> colorSpecs = [];
    private readonly List<string> ignoreFiles = [];
    private readonly List<string> preprocessorGlobs = [];
    private readonly List<CliTypeChange> typeChanges = [];
    private ReadOnlyMemory<byte> fieldMatchSeparator = DefaultFieldMatchSeparator;
    private ReadOnlyMemory<byte> fieldContextSeparator = DefaultFieldContextSeparator;
    private ReadOnlyMemory<byte> contextSeparator = DefaultContextSeparator;
    private bool beforeContextSpecified;
    private bool afterContextSpecified;

    /// <summary>
    /// Gets the positional arguments.
    /// </summary>
    public IReadOnlyList<OsString> Positional => positional;

    /// <summary>
    /// Gets the explicit search patterns parsed from <c>-e</c>/<c>--regexp</c>.
    /// </summary>
    public IReadOnlyList<OsString> Patterns => patterns;

    /// <summary>
    /// Gets explicit pattern sources in command-line order.
    /// </summary>
    public IReadOnlyList<CliPatternSource> PatternSources => patternSources;

    /// <summary>
    /// Gets the explicit traversal override glob patterns.
    /// </summary>
    public IReadOnlyList<CliGlobPattern> GlobPatterns => globPatterns;

    /// <summary>
    /// Gets the ordered user color specifications parsed from <c>--colors</c>.
    /// </summary>
    public IReadOnlyList<string> ColorSpecs => colorSpecs;

    /// <summary>
    /// Gets the explicit ignore files parsed from <c>--ignore-file</c>.
    /// </summary>
    public IReadOnlyList<string> IgnoreFiles => ignoreFiles;

    /// <summary>
    /// Gets the ordered preprocessor filter globs parsed from <c>--pre-glob</c>.
    /// </summary>
    public IReadOnlyList<string> PreprocessorGlobs => preprocessorGlobs;

    /// <summary>
    /// Gets the ordered file type changes parsed from the command line.
    /// </summary>
    public IReadOnlyList<CliTypeChange> TypeChanges => typeChanges;

    /// <summary>
    /// Gets a value indicating whether matching output should include line numbers.
    /// </summary>
    public bool LineNumber { get; private set; }

    /// <summary>
    /// Gets a value indicating whether line-number behavior was explicitly configured.
    /// </summary>
    public bool LineNumberSpecified { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matching output should include columns.
    /// </summary>
    public bool Column { get; private set; }

    /// <summary>
    /// Gets a value indicating whether column behavior was explicitly configured.
    /// </summary>
    public bool ColumnSpecified { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matching output should include byte offsets.
    /// </summary>
    public bool ByteOffset { get; private set; }

    /// <summary>
    /// Gets the selected search output mode.
    /// </summary>
    public CliSearchMode SearchMode { get; private set; }

    /// <summary>
    /// Gets the generated artifact mode, or <see langword="null" /> when normal search behavior should run.
    /// </summary>
    public CliGenerateMode? GenerateMode { get; private set; }

    /// <summary>
    /// Gets the selected case-matching mode.
    /// </summary>
    public CliCaseMode CaseMode { get; private set; }

    /// <summary>
    /// Gets the selected color output mode.
    /// </summary>
    public CliColorMode ColorMode { get; private set; }

    /// <summary>
    /// Gets the explicit filename-prefix mode, or <see langword="null" /> for automatic behavior.
    /// </summary>
    public bool? WithFilename { get; private set; }

    /// <summary>
    /// Gets a value indicating whether patterns should be treated as fixed strings.
    /// </summary>
    public bool FixedStrings { get; private set; }

    /// <summary>
    /// Gets the requested DFA cache size limit, or <see langword="null" /> when the default should be used.
    /// </summary>
    public ulong? DfaSizeLimit { get; private set; }

    /// <summary>
    /// Gets the requested compiled regex size limit, or <see langword="null" /> when the default should be used.
    /// </summary>
    public ulong? RegexSizeLimit { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matches may span line terminators.
    /// </summary>
    public bool Multiline { get; private set; }

    /// <summary>
    /// Gets a value indicating whether dot matches line terminators in multiline mode.
    /// </summary>
    public bool MultilineDotall { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Unicode regex mode is enabled.
    /// </summary>
    public bool Unicode { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether binary bytes should be searched and printed as text.
    /// </summary>
    public bool TextMode { get; private set; }

    /// <summary>
    /// Gets a value indicating whether binary files should be searched with suppression.
    /// </summary>
    public bool SearchBinaryFiles { get; private set; }

    /// <summary>
    /// Gets a value indicating whether known compressed files should be decompressed before searching.
    /// </summary>
    public bool SearchZip { get; private set; }

    /// <summary>
    /// Gets the requested input encoding mode.
    /// </summary>
    public CliEncodingMode EncodingMode { get; private set; }

    /// <summary>
    /// Gets the preprocessor command to run for searched files, or <see langword="null" /> when disabled.
    /// </summary>
    public string? Preprocessor { get; private set; }

    /// <summary>
    /// Gets a value indicating whether stdout output should be suppressed.
    /// </summary>
    public bool Quiet { get; private set; }

    /// <summary>
    /// Gets a value indicating whether search statistics should be printed.
    /// </summary>
    public bool Stats { get; private set; }

    /// <summary>
    /// Gets a value indicating whether searching should stop after the first non-matching line following a match.
    /// </summary>
    public bool StopOnNonmatch { get; private set; }

    /// <summary>
    /// Gets a value indicating whether CRLF should be treated as a line terminator.
    /// </summary>
    public bool Crlf { get; private set; }

    /// <summary>
    /// Gets a value indicating whether NUL should be treated as a line terminator.
    /// </summary>
    public bool NullData { get; private set; }

    /// <summary>
    /// Gets a value indicating whether only matching bytes should be printed.
    /// </summary>
    public bool OnlyMatching { get; private set; }

    /// <summary>
    /// Gets the replacement bytes for matching text, or <see langword="null" /> when replacement is disabled.
    /// </summary>
    public ReadOnlyMemory<byte>? Replacement { get; private set; }

    /// <summary>
    /// Gets a value indicating whether output should use vim-compatible match fields.
    /// </summary>
    public bool Vimgrep { get; private set; }

    /// <summary>
    /// Gets a value indicating whether count output should include zero counts.
    /// </summary>
    public bool IncludeZero { get; private set; }

    /// <summary>
    /// Gets a value indicating whether printed paths should be followed by a NUL byte.
    /// </summary>
    public bool NullPathTerminator { get; private set; }

    /// <summary>
    /// Gets the path separator byte to use when printing paths, or <see langword="null" /> for platform defaults.
    /// </summary>
    public byte? PathSeparator { get; private set; }

    /// <summary>
    /// Gets the field separator used for matching lines.
    /// </summary>
    public ReadOnlyMemory<byte> FieldMatchSeparator => fieldMatchSeparator;

    /// <summary>
    /// Gets the field separator used for contextual lines.
    /// </summary>
    public ReadOnlyMemory<byte> FieldContextSeparator => fieldContextSeparator;

    /// <summary>
    /// Gets the separator used between non-contiguous context groups.
    /// </summary>
    public ReadOnlyMemory<byte> ContextSeparator => contextSeparator;

    /// <summary>
    /// Gets a value indicating whether non-contiguous context separators should be printed.
    /// </summary>
    public bool ContextSeparatorEnabled { get; private set; } = true;


    /// <summary>
    /// Gets the maximum number of matching lines per searched file, or <see langword="null" /> for no limit.
    /// </summary>
    public ulong? MaxCount { get; private set; }

    /// <summary>
    /// Gets the maximum displayed line length in bytes, or <see langword="null" /> for no limit.
    /// </summary>
    public ulong? MaxColumns { get; private set; }

    /// <summary>
    /// Gets a value indicating whether long-line output should include a preview.
    /// </summary>
    public bool MaxColumnsPreview { get; private set; }

    /// <summary>
    /// Gets the number of context lines to print before each matching line.
    /// </summary>
    public ulong BeforeContext { get; private set; }

    /// <summary>
    /// Gets the number of context lines to print after each matching line.
    /// </summary>
    public ulong AfterContext { get; private set; }

    /// <summary>
    /// Gets a value indicating whether every searched line should be printed.
    /// </summary>
    public bool Passthru { get; private set; }

    /// <summary>
    /// Gets the maximum directory traversal depth, or <see langword="null" /> for no limit.
    /// </summary>
    public ulong? MaxDepth { get; private set; }

    /// <summary>
    /// Gets the maximum file size to search, or <see langword="null" /> for no limit.
    /// </summary>
    public ulong? MaxFileSize { get; private set; }

    /// <summary>
    /// Gets the selected result sort mode, or <see langword="null" /> when output should not be sorted.
    /// </summary>
    public CliSortMode? SortMode { get; private set; }

    /// <summary>
    /// Gets the requested number of search threads, or <see langword="null" /> when the default should be used.
    /// </summary>
    public ulong? Threads { get; private set; }

    /// <summary>
    /// Gets the requested stdout buffering mode.
    /// </summary>
    public CliBufferMode BufferMode { get; private set; }

    /// <summary>
    /// Gets the requested memory-map search mode.
    /// </summary>
    public CliMmapMode MmapMode { get; private set; }

    /// <summary>
    /// Gets the requested regex engine.
    /// </summary>
    public CliRegexEngine RegexEngine { get; private set; }

    /// <summary>
    /// Gets the requested diagnostic logging mode, or <see langword="null" /> when logging is disabled.
    /// </summary>
    public CliLoggingMode? LoggingMode { get; private set; }

    /// <summary>
    /// Gets the requested hybrid-regex setting, or <see langword="null" /> when the default should be used.
    /// </summary>
    public bool? AutoHybridRegex { get; private set; }

    /// <summary>
    /// Gets a value indicating whether PCRE2 Unicode mode is enabled.
    /// </summary>
    public bool Pcre2Unicode { get; private set; } = true;

    /// <summary>
    /// Gets the program used to discover the hostname for hyperlinks, or <see langword="null" /> for platform detection.
    /// </summary>
    public string? HostnameBin { get; private set; }

    /// <summary>
    /// Gets the requested hyperlink format, or <see langword="null" /> when hyperlinks are disabled.
    /// </summary>
    public string? HyperlinkFormat { get; private set; }

    /// <summary>
    /// Gets a value indicating whether non-fatal search diagnostics should be printed.
    /// </summary>
    public bool Messages { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether file type definitions should be printed instead of searching.
    /// </summary>
    public bool TypeList { get; private set; }

    /// <summary>
    /// Gets a value indicating whether printed lines should omit leading ASCII whitespace.
    /// </summary>
    public bool Trim { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matching output should be grouped under path headings.
    /// </summary>
    public bool Heading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether heading behavior was explicitly configured.
    /// </summary>
    public bool HeadingSpecified { get; private set; }

    /// <summary>
    /// Gets a value indicating whether line matching should be inverted.
    /// </summary>
    public bool InvertMatch { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matches must span the full line.
    /// </summary>
    public bool LineRegexp { get; private set; }

    /// <summary>
    /// Gets a value indicating whether matches must be surrounded by word boundaries.
    /// </summary>
    public bool WordRegexp { get; private set; }

    /// <summary>
    /// Gets a value indicating whether hidden files and directories should be included.
    /// </summary>
    public bool IncludeHidden { get; private set; }

    /// <summary>
    /// Gets a value indicating whether symbolic links should be followed during traversal.
    /// </summary>
    public bool FollowLinks { get; private set; }

    /// <summary>
    /// Gets a value indicating whether ignore files should be respected.
    /// </summary>
    public bool RespectIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether <c>.ignore</c> and <c>.rgignore</c> files should be respected.
    /// </summary>
    public bool RespectDotIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether source-control ignore files should be respected.
    /// </summary>
    public bool RespectGitIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether source-control exclude files should be respected.
    /// </summary>
    public bool RespectGitExcludeFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether global ignore files should be respected.
    /// </summary>
    public bool RespectGlobalIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether ignore files from parent directories should be respected.
    /// </summary>
    public bool RespectParentIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether ignore-file diagnostics should be printed.
    /// </summary>
    public bool IgnoreMessages { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether explicitly supplied ignore files should be respected.
    /// </summary>
    public bool RespectExplicitIgnoreFiles { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether source-control ignore files require a repository marker.
    /// </summary>
    public bool RequireGitRepository { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether ignore files should be matched case-insensitively.
    /// </summary>
    public bool IgnoreFileCaseInsensitive { get; private set; }

    /// <summary>
    /// Gets a value indicating whether traversal should stay on one file system.
    /// </summary>
    public bool OneFileSystem { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <c>-g</c>/<c>--glob</c> patterns should be matched case-insensitively.
    /// </summary>
    public bool GlobCaseInsensitive { get; private set; }

    /// <summary>
    /// Gets the number of unrestricted levels requested by <c>-u</c>/<c>--unrestricted</c>.
    /// </summary>
    public int UnrestrictedCount { get; private set; }

    /// <summary>
    /// Adds a positional argument.
    /// </summary>
    /// <param name="argument">The argument to add.</param>
    public void AddPositional(OsString argument)
    {
        positional.Add(argument);
    }

    /// <summary>
    /// Adds an explicit search pattern.
    /// </summary>
    /// <param name="pattern">The pattern to add.</param>
    public void AddPattern(OsString pattern)
    {
        patterns.Add(pattern);
        patternSources.Add(CliPatternSource.Pattern(pattern));
    }

    /// <summary>
    /// Adds an explicit search pattern file.
    /// </summary>
    /// <param name="path">The pattern-file path to add.</param>
    public void AddPatternFile(OsString path)
    {
        patternSources.Add(CliPatternSource.File(path));
    }

    /// <summary>
    /// Adds a traversal override glob pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <param name="caseInsensitive">Whether this glob is intrinsically case-insensitive.</param>
    public void AddGlobPattern(string pattern, bool caseInsensitive)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        globPatterns.Add(new CliGlobPattern(pattern, caseInsensitive));
    }

    /// <summary>
    /// Adds a user color specification.
    /// </summary>
    /// <param name="spec">The color specification.</param>
    public void AddColorSpec(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        colorSpecs.Add(spec);
    }

    /// <summary>
    /// Adds an explicit ignore file path.
    /// </summary>
    /// <param name="path">The ignore file path.</param>
    public void AddIgnoreFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ignoreFiles.Add(path);
    }

    /// <summary>
    /// Adds a preprocessor glob filter.
    /// </summary>
    /// <param name="pattern">The preprocessor glob pattern.</param>
    public void AddPreprocessorGlob(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        preprocessorGlobs.Add(pattern);
    }

    /// <summary>
    /// Adds an ordered file type change.
    /// </summary>
    /// <param name="change">The file type change.</param>
    public void AddTypeChange(CliTypeChange change)
    {
        typeChanges.Add(change);
    }

    /// <summary>
    /// Enables or disables line numbers for matching output.
    /// </summary>
    /// <param name="yes">Whether line numbers should be printed.</param>
    public void SetLineNumber(bool yes)
    {
        LineNumber = yes;
        LineNumberSpecified = true;
    }

    /// <summary>
    /// Enables or disables columns for matching output.
    /// </summary>
    /// <param name="yes">Whether columns should be printed.</param>
    public void SetColumn(bool yes)
    {
        Column = yes;
        ColumnSpecified = true;
    }

    /// <summary>
    /// Enables or disables byte offsets for matching output.
    /// </summary>
    /// <param name="yes">Whether byte offsets should be printed.</param>
    public void SetByteOffset(bool yes)
    {
        ByteOffset = yes;
    }

    /// <summary>
    /// Sets the search output mode.
    /// </summary>
    /// <param name="searchMode">The search output mode.</param>
    public void SetSearchMode(CliSearchMode searchMode)
    {
        GenerateMode = null;
        SearchMode = searchMode;
    }

    /// <summary>
    /// Sets the generated artifact mode.
    /// </summary>
    /// <param name="generateMode">The generated artifact mode.</param>
    public void SetGenerateMode(CliGenerateMode generateMode)
    {
        GenerateMode = generateMode;
    }

    /// <summary>
    /// Reverts JSON output mode to standard output mode when JSON is currently selected.
    /// </summary>
    public void ClearJsonMode()
    {
        if (SearchMode == CliSearchMode.Json)
        {
            SearchMode = CliSearchMode.Standard;
        }
    }

    /// <summary>
    /// Sets the case-matching mode.
    /// </summary>
    /// <param name="caseMode">The case-matching mode.</param>
    public void SetCaseMode(CliCaseMode caseMode)
    {
        CaseMode = caseMode;
    }

    /// <summary>
    /// Sets the color output mode.
    /// </summary>
    /// <param name="colorMode">The color output mode.</param>
    public void SetColorMode(CliColorMode colorMode)
    {
        ColorMode = colorMode;
    }

    /// <summary>
    /// Applies ripgrep's pretty output alias.
    /// </summary>
    public void SetPretty()
    {
        ColorMode = CliColorMode.Always;
        Heading = true;
        HeadingSpecified = true;
        LineNumber = true;
        LineNumberSpecified = true;
    }

    /// <summary>
    /// Sets explicit filename-prefix behavior.
    /// </summary>
    /// <param name="yes">Whether filenames should be printed with matching output.</param>
    public void SetWithFilename(bool yes)
    {
        WithFilename = yes;
    }

    /// <summary>
    /// Enables or disables fixed-string pattern mode.
    /// </summary>
    /// <param name="yes">Whether fixed-string pattern mode should be enabled.</param>
    public void SetFixedStrings(bool yes)
    {
        FixedStrings = yes;
    }

    /// <summary>
    /// Sets the DFA cache size limit.
    /// </summary>
    /// <param name="bytes">The limit in bytes.</param>
    public void SetDfaSizeLimit(ulong bytes)
    {
        DfaSizeLimit = bytes;
    }

    /// <summary>
    /// Sets the compiled regex size limit.
    /// </summary>
    /// <param name="bytes">The limit in bytes.</param>
    public void SetRegexSizeLimit(ulong bytes)
    {
        RegexSizeLimit = bytes;
    }

    /// <summary>
    /// Enables or disables multiline mode.
    /// </summary>
    /// <param name="yes">Whether multiline mode should be enabled.</param>
    public void SetMultiline(bool yes)
    {
        Multiline = yes;
        if (yes)
        {
            StopOnNonmatch = false;
        }
    }

    /// <summary>
    /// Enables or disables multiline dot-all mode.
    /// </summary>
    /// <param name="yes">Whether dot-all mode should be enabled for multiline searches.</param>
    public void SetMultilineDotall(bool yes)
    {
        MultilineDotall = yes;
    }

    /// <summary>
    /// Enables or disables Unicode regex mode.
    /// </summary>
    /// <param name="yes">Whether Unicode mode should be enabled.</param>
    public void SetUnicode(bool yes)
    {
        Unicode = yes;
    }

    /// <summary>
    /// Enables or disables text mode for binary files.
    /// </summary>
    /// <param name="yes">Whether binary data should be treated as text.</param>
    public void SetTextMode(bool yes)
    {
        TextMode = yes;
        if (yes)
        {
            SearchBinaryFiles = false;
        }
    }

    /// <summary>
    /// Enables or disables binary file searching with suppression.
    /// </summary>
    /// <param name="yes">Whether binary file searching should be enabled.</param>
    public void SetSearchBinaryFiles(bool yes)
    {
        SearchBinaryFiles = yes;
        TextMode = false;
    }

    /// <summary>
    /// Enables or disables compressed-file searching.
    /// </summary>
    /// <param name="yes">Whether known compressed files should be decompressed before searching.</param>
    public void SetSearchZip(bool yes)
    {
        SearchZip = yes;
        if (yes)
        {
            Preprocessor = null;
        }
    }

    /// <summary>
    /// Sets the input encoding mode.
    /// </summary>
    /// <param name="encodingMode">The input encoding mode.</param>
    public void SetEncodingMode(CliEncodingMode encodingMode)
    {
        EncodingMode = encodingMode;
    }

    /// <summary>
    /// Sets the preprocessor command to run for searched files.
    /// </summary>
    /// <param name="command">The preprocessor command, or <see langword="null" /> to disable preprocessing.</param>
    public void SetPreprocessor(string? command)
    {
        Preprocessor = string.IsNullOrEmpty(command) ? null : command;
        if (Preprocessor is not null)
        {
            SearchZip = false;
        }
    }

    /// <summary>
    /// Enables or disables quiet mode.
    /// </summary>
    /// <param name="yes">Whether quiet mode should be enabled.</param>
    public void SetQuiet(bool yes)
    {
        Quiet = yes;
    }

    /// <summary>
    /// Enables or disables search statistics output.
    /// </summary>
    /// <param name="yes">Whether search statistics should be printed.</param>
    public void SetStats(bool yes)
    {
        Stats = yes;
    }

    /// <summary>
    /// Enables or disables stop-on-nonmatch mode.
    /// </summary>
    /// <param name="yes">Whether searching should stop after the first non-matching line following a match.</param>
    public void SetStopOnNonmatch(bool yes)
    {
        StopOnNonmatch = yes;
    }

    /// <summary>
    /// Enables or disables CRLF line terminator semantics.
    /// </summary>
    /// <param name="yes">Whether CRLF mode should be enabled.</param>
    public void SetCrlf(bool yes)
    {
        Crlf = yes;
        if (yes)
        {
            NullData = false;
        }
    }

    /// <summary>
    /// Enables or disables NUL data line terminator semantics.
    /// </summary>
    /// <param name="yes">Whether NUL data mode should be enabled.</param>
    public void SetNullData(bool yes)
    {
        NullData = yes;
    }

    /// <summary>
    /// Enables or disables only-matching output.
    /// </summary>
    /// <param name="yes">Whether only matching bytes should be printed.</param>
    public void SetOnlyMatching(bool yes)
    {
        OnlyMatching = yes;
    }

    /// <summary>
    /// Sets replacement bytes for matching text.
    /// </summary>
    /// <param name="replacement">The replacement bytes.</param>
    public void SetReplacement(ReadOnlySpan<byte> replacement)
    {
        Replacement = replacement.ToArray();
    }

    /// <summary>
    /// Enables or disables vim-compatible match output.
    /// </summary>
    /// <param name="yes">Whether vimgrep output should be enabled.</param>
    public void SetVimgrep(bool yes)
    {
        Vimgrep = yes;
    }

    /// <summary>
    /// Enables or disables zero count output.
    /// </summary>
    /// <param name="yes">Whether zero counts should be printed.</param>
    public void SetIncludeZero(bool yes)
    {
        IncludeZero = yes;
    }

    /// <summary>
    /// Enables or disables NUL path terminators.
    /// </summary>
    /// <param name="yes">Whether printed paths should be followed by a NUL byte.</param>
    public void SetNullPathTerminator(bool yes)
    {
        NullPathTerminator = yes;
    }

    /// <summary>
    /// Sets the path separator byte to use when printing paths.
    /// </summary>
    /// <param name="separator">The replacement separator byte, or <see langword="null" /> for platform defaults.</param>
    public void SetPathSeparator(byte? separator)
    {
        PathSeparator = separator;
    }

    /// <summary>
    /// Sets the field separator used for matching lines.
    /// </summary>
    /// <param name="separator">The separator bytes.</param>
    public void SetFieldMatchSeparator(ReadOnlySpan<byte> separator)
    {
        fieldMatchSeparator = separator.ToArray();
    }

    /// <summary>
    /// Sets the field separator used for contextual lines.
    /// </summary>
    /// <param name="separator">The separator bytes.</param>
    public void SetFieldContextSeparator(ReadOnlySpan<byte> separator)
    {
        fieldContextSeparator = separator.ToArray();
    }

    /// <summary>
    /// Sets the separator used between non-contiguous context groups.
    /// </summary>
    /// <param name="separator">The separator bytes.</param>
    public void SetContextSeparator(ReadOnlySpan<byte> separator)
    {
        contextSeparator = separator.ToArray();
        ContextSeparatorEnabled = true;
    }

    /// <summary>
    /// Enables or disables non-contiguous context separators.
    /// </summary>
    /// <param name="yes">Whether context separators should be printed.</param>
    public void SetContextSeparatorEnabled(bool yes)
    {
        ContextSeparatorEnabled = yes;
    }

    /// <summary>
    /// Sets the maximum number of matching lines per searched file.
    /// </summary>
    /// <param name="count">The maximum matching-line count.</param>
    public void SetMaxCount(ulong count)
    {
        MaxCount = count;
    }

    /// <summary>
    /// Sets the maximum displayed line length.
    /// </summary>
    /// <param name="columns">The maximum displayed line length in bytes.</param>
    public void SetMaxColumns(ulong columns)
    {
        MaxColumns = columns;
    }

    /// <summary>
    /// Enables or disables preview output for lines that exceed the maximum displayed length.
    /// </summary>
    /// <param name="yes">Whether long-line preview output should be enabled.</param>
    public void SetMaxColumnsPreview(bool yes)
    {
        MaxColumnsPreview = yes;
    }

    /// <summary>
    /// Sets the number of context lines to print after each matching line.
    /// </summary>
    /// <param name="count">The number of context lines.</param>
    public void SetAfterContext(ulong count)
    {
        AfterContext = count;
        afterContextSpecified = true;
        Passthru = false;
    }

    /// <summary>
    /// Sets the number of context lines to print before each matching line.
    /// </summary>
    /// <param name="count">The number of context lines.</param>
    public void SetBeforeContext(ulong count)
    {
        BeforeContext = count;
        beforeContextSpecified = true;
        Passthru = false;
    }

    /// <summary>
    /// Sets the default number of context lines to print around matching lines.
    /// </summary>
    /// <param name="count">The number of context lines.</param>
    public void SetContext(ulong count)
    {
        if (!beforeContextSpecified)
        {
            BeforeContext = count;
        }

        if (!afterContextSpecified)
        {
            AfterContext = count;
        }

        Passthru = false;
    }

    /// <summary>
    /// Enables or disables passthrough output.
    /// </summary>
    /// <param name="yes">Whether every searched line should be printed.</param>
    public void SetPassthru(bool yes)
    {
        Passthru = yes;
    }

    /// <summary>
    /// Sets the maximum directory traversal depth.
    /// </summary>
    /// <param name="depth">The maximum directory traversal depth.</param>
    public void SetMaxDepth(ulong depth)
    {
        MaxDepth = depth;
    }

    /// <summary>
    /// Sets the maximum file size to search.
    /// </summary>
    /// <param name="bytes">The maximum file size in bytes.</param>
    public void SetMaxFileSize(ulong bytes)
    {
        MaxFileSize = bytes;
    }

    /// <summary>
    /// Sets the result sort mode.
    /// </summary>
    /// <param name="sortMode">The sort mode, or <see langword="null" /> to disable sorting.</param>
    public void SetSortMode(CliSortMode? sortMode)
    {
        SortMode = sortMode;
    }

    /// <summary>
    /// Sets the requested number of search threads.
    /// </summary>
    /// <param name="threads">The requested number of search threads.</param>
    public void SetThreads(ulong threads)
    {
        Threads = threads == 0 ? null : threads;
    }

    /// <summary>
    /// Sets the stdout buffering mode.
    /// </summary>
    /// <param name="mode">The buffering mode.</param>
    public void SetBufferMode(CliBufferMode mode)
    {
        BufferMode = mode;
    }

    /// <summary>
    /// Sets the memory-map search mode.
    /// </summary>
    /// <param name="mode">The memory-map mode.</param>
    public void SetMmapMode(CliMmapMode mode)
    {
        MmapMode = mode;
    }

    /// <summary>
    /// Sets the diagnostic logging mode.
    /// </summary>
    /// <param name="mode">The requested logging mode.</param>
    public void SetLoggingMode(CliLoggingMode mode)
    {
        LoggingMode = mode;
    }

    /// <summary>
    /// Sets the regex engine.
    /// </summary>
    /// <param name="engine">The regex engine.</param>
    public void SetRegexEngine(CliRegexEngine engine)
    {
        RegexEngine = engine;
    }

    /// <summary>
    /// Enables or disables automatic hybrid regex use.
    /// </summary>
    /// <param name="yes">Whether automatic hybrid regex use should be enabled.</param>
    public void SetAutoHybridRegex(bool yes)
    {
        AutoHybridRegex = yes;
        RegexEngine = yes ? CliRegexEngine.Auto : CliRegexEngine.Default;
    }

    /// <summary>
    /// Enables or disables PCRE2 Unicode mode.
    /// </summary>
    /// <param name="yes">Whether PCRE2 Unicode mode should be enabled.</param>
    public void SetPcre2Unicode(bool yes)
    {
        Pcre2Unicode = yes;
    }

    /// <summary>
    /// Sets the hostname discovery program for hyperlink formatting.
    /// </summary>
    /// <param name="program">The program to run.</param>
    public void SetHostnameBin(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        HostnameBin = program;
    }

    /// <summary>
    /// Sets the hyperlink output format.
    /// </summary>
    /// <param name="format">The hyperlink format.</param>
    public void SetHyperlinkFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        HyperlinkFormat = format;
    }

    /// <summary>
    /// Enables or disables non-fatal search diagnostic messages.
    /// </summary>
    /// <param name="yes">Whether messages should be printed.</param>
    public void SetMessages(bool yes)
    {
        Messages = yes;
    }

    /// <summary>
    /// Enables or disables type-list mode.
    /// </summary>
    /// <param name="yes">Whether type-list mode should be enabled.</param>
    public void SetTypeList(bool yes)
    {
        TypeList = yes;
    }

    /// <summary>
    /// Enables or disables trimming leading ASCII whitespace from printed lines.
    /// </summary>
    /// <param name="yes">Whether printed lines should be trimmed.</param>
    public void SetTrim(bool yes)
    {
        Trim = yes;
    }

    /// <summary>
    /// Enables or disables path headings in matching output.
    /// </summary>
    /// <param name="yes">Whether path headings should be printed.</param>
    public void SetHeading(bool yes)
    {
        Heading = yes;
        HeadingSpecified = true;
    }

    /// <summary>
    /// Enables or disables inverted line matching.
    /// </summary>
    /// <param name="yes">Whether inverted line matching should be enabled.</param>
    public void SetInvertMatch(bool yes)
    {
        InvertMatch = yes;
    }

    /// <summary>
    /// Enables or disables full-line matching.
    /// </summary>
    /// <param name="yes">Whether full-line matching should be enabled.</param>
    public void SetLineRegexp(bool yes)
    {
        LineRegexp = yes;
        if (yes)
        {
            WordRegexp = false;
        }
    }

    /// <summary>
    /// Enables or disables word-boundary matching.
    /// </summary>
    /// <param name="yes">Whether word-boundary matching should be enabled.</param>
    public void SetWordRegexp(bool yes)
    {
        WordRegexp = yes;
        if (yes)
        {
            LineRegexp = false;
        }
    }

    /// <summary>
    /// Enables or disables hidden file traversal.
    /// </summary>
    /// <param name="yes">Whether hidden files and directories should be included.</param>
    public void SetIncludeHidden(bool yes)
    {
        IncludeHidden = yes;
    }

    /// <summary>
    /// Enables or disables symbolic-link traversal.
    /// </summary>
    /// <param name="yes">Whether symbolic links should be followed.</param>
    public void SetFollowLinks(bool yes)
    {
        FollowLinks = yes;
    }

    /// <summary>
    /// Enables or disables ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether ignore files should be respected.</param>
    public void SetRespectIgnoreFiles(bool yes)
    {
        RespectIgnoreFiles = yes;
        RespectDotIgnoreFiles = yes;
        RespectGitIgnoreFiles = yes;
        RespectGitExcludeFiles = yes;
        RespectGlobalIgnoreFiles = yes;
        RespectParentIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables dot ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether <c>.ignore</c> and <c>.rgignore</c> files should be respected.</param>
    public void SetRespectDotIgnoreFiles(bool yes)
    {
        RespectDotIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables source-control ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether source-control ignore files should be respected.</param>
    public void SetRespectGitIgnoreFiles(bool yes)
    {
        RespectGitIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables source-control exclude-file filtering.
    /// </summary>
    /// <param name="yes">Whether source-control exclude files should be respected.</param>
    public void SetRespectGitExcludeFiles(bool yes)
    {
        RespectGitExcludeFiles = yes;
    }

    /// <summary>
    /// Enables or disables global ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether global ignore files should be respected.</param>
    public void SetRespectGlobalIgnoreFiles(bool yes)
    {
        RespectGlobalIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables parent ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether parent ignore files should be respected.</param>
    public void SetRespectParentIgnoreFiles(bool yes)
    {
        RespectParentIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables explicit ignore-file filtering.
    /// </summary>
    /// <param name="yes">Whether explicitly supplied ignore files should be respected.</param>
    public void SetRespectExplicitIgnoreFiles(bool yes)
    {
        RespectExplicitIgnoreFiles = yes;
    }

    /// <summary>
    /// Enables or disables ignore-file diagnostic messages.
    /// </summary>
    /// <param name="yes">Whether ignore-file diagnostics should be printed.</param>
    public void SetIgnoreMessages(bool yes)
    {
        IgnoreMessages = yes;
    }

    /// <summary>
    /// Enables or disables requiring a repository marker for source-control ignore files.
    /// </summary>
    /// <param name="yes">Whether a repository marker should be required.</param>
    public void SetRequireGitRepository(bool yes)
    {
        RequireGitRepository = yes;
    }

    /// <summary>
    /// Enables or disables case-insensitive ignore-file matching.
    /// </summary>
    /// <param name="yes">Whether ignore files should match case-insensitively.</param>
    public void SetIgnoreFileCaseInsensitive(bool yes)
    {
        IgnoreFileCaseInsensitive = yes;
    }

    /// <summary>
    /// Enables or disables same-file-system traversal.
    /// </summary>
    /// <param name="yes">Whether traversal should stay on one file system.</param>
    public void SetOneFileSystem(bool yes)
    {
        OneFileSystem = yes;
    }

    /// <summary>
    /// Enables or disables case-insensitive matching for <c>-g</c>/<c>--glob</c> patterns.
    /// </summary>
    /// <param name="yes">Whether glob patterns should match case-insensitively.</param>
    public void SetGlobCaseInsensitive(bool yes)
    {
        GlobCaseInsensitive = yes;
    }

    /// <summary>
    /// Adds one unrestricted filtering level.
    /// </summary>
    public void AddUnrestrictedLevel()
    {
        UnrestrictedCount++;
        if (UnrestrictedCount == 1)
        {
            SetRespectIgnoreFiles(false);
            return;
        }

        if (UnrestrictedCount == 2)
        {
            SetIncludeHidden(true);
            return;
        }

        SetSearchBinaryFiles(true);
    }
}
