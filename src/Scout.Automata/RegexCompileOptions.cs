namespace Scout;

/// <summary>
/// Describes the effective options used to compile a regex syntax subtree.
/// </summary>
/// <param name="caseInsensitive">Whether matching ignores case.</param>
/// <param name="swapGreed">Whether repetition greediness is reversed.</param>
/// <param name="multiLine">Whether line anchors match at record boundaries.</param>
/// <param name="dotMatchesNewline">Whether dot normally matches line terminators.</param>
/// <param name="crlf">Whether carriage return and line feed form the line-terminator family.</param>
/// <param name="lineTerminator">The line terminator used when CRLF mode is disabled.</param>
/// <param name="utf8">Whether matching observes UTF-8 scalar boundaries.</param>
/// <param name="unicodeClasses">Whether character classes use Unicode semantics.</param>
/// <param name="specializationMode">The requested specialization mode.</param>
/// <param name="excludeLineTerminators">Whether every consuming atom excludes the configured record terminator.</param>
/// <param name="excludeCrLf">Whether exclusion treats CR and LF as one immutable record-terminator family.</param>
/// <param name="excludedLineTerminator">The record byte excluded from consuming atoms, or <see langword="null" /> to use <paramref name="lineTerminator" />.</param>
/// <param name="allowRawPatternSpecializations">Whether specializations may rescan the original pattern bytes.</param>
internal readonly struct RegexCompileOptions(
    bool caseInsensitive,
    bool swapGreed,
    bool multiLine,
    bool dotMatchesNewline,
    bool crlf = false,
    byte lineTerminator = (byte)'\n',
    bool utf8 = true,
    bool unicodeClasses = true,
    RegexSpecializationMode? specializationMode = null,
    bool excludeLineTerminators = false,
    bool? excludeCrLf = null,
    byte? excludedLineTerminator = null,
    bool allowRawPatternSpecializations = true)
{
    /// <summary>
    /// Gets a value indicating whether matching ignores case.
    /// </summary>
    public bool CaseInsensitive { get; } = caseInsensitive;

    /// <summary>
    /// Gets a value indicating whether repetition greediness is reversed.
    /// </summary>
    public bool SwapGreed { get; } = swapGreed;

    /// <summary>
    /// Gets a value indicating whether line anchors match at record boundaries.
    /// </summary>
    public bool MultiLine { get; } = multiLine;

    /// <summary>
    /// Gets a value indicating whether dot normally matches line terminators.
    /// </summary>
    public bool DotMatchesNewline { get; } = dotMatchesNewline;

    /// <summary>
    /// Gets a value indicating whether carriage return and line feed form the line-terminator family.
    /// </summary>
    public bool Crlf { get; } = crlf;

    /// <summary>
    /// Gets the line terminator used when CRLF mode is disabled.
    /// </summary>
    public byte LineTerminator { get; } = lineTerminator;

    /// <summary>
    /// Gets a value indicating whether matching observes UTF-8 scalar boundaries.
    /// </summary>
    public bool Utf8 { get; } = utf8;

    /// <summary>
    /// Gets a value indicating whether character classes use Unicode semantics.
    /// </summary>
    public bool UnicodeClasses { get; } = unicodeClasses;

    /// <summary>
    /// Gets the specialization mode.
    /// </summary>
    public RegexSpecializationMode SpecializationMode { get; } = specializationMode ?? RegexSpecializationModeDefaults.Current;

    /// <summary>
    /// Gets a value indicating whether every consuming atom excludes the configured record terminator.
    /// </summary>
    public bool ExcludeLineTerminators { get; } = excludeLineTerminators;

    /// <summary>
    /// Gets a value indicating whether exclusion treats CR and LF as one immutable record-terminator family.
    /// </summary>
    public bool ExcludeCrLf { get; } = excludeCrLf ?? (excludeLineTerminators && crlf);

    /// <summary>
    /// Gets the record byte excluded from consuming atoms.
    /// </summary>
    public byte ExcludedLineTerminator { get; } = excludedLineTerminator ?? lineTerminator;

    /// <summary>
    /// Gets a value indicating whether specializations may rescan the original pattern bytes.
    /// </summary>
    public bool AllowRawPatternSpecializations { get; } = allowRawPatternSpecializations;

    /// <summary>
    /// Applies scoped regex flags while preserving non-flag compilation policy.
    /// </summary>
    /// <param name="enabledFlags">The flags enabled by the scope.</param>
    /// <param name="disabledFlags">The flags disabled by the scope.</param>
    /// <returns>The effective options for the scope.</returns>
    public RegexCompileOptions Apply(string enabledFlags, string disabledFlags)
    {
        bool effectiveCaseInsensitive = CaseInsensitive;
        bool effectiveSwapGreed = SwapGreed;
        bool effectiveMultiLine = MultiLine;
        bool effectiveDotMatchesNewline = DotMatchesNewline;
        bool effectiveCrlf = Crlf;
        bool effectiveUtf8 = Utf8;
        bool effectiveUnicodeClasses = UnicodeClasses;
        for (int index = 0; index < enabledFlags.Length; index++)
        {
            ApplyFlag(
                enabledFlags[index],
                enabled: true,
                ref effectiveCaseInsensitive,
                ref effectiveSwapGreed,
                ref effectiveMultiLine,
                ref effectiveDotMatchesNewline,
                ref effectiveCrlf,
                ref effectiveUtf8,
                ref effectiveUnicodeClasses);
        }

        for (int index = 0; index < disabledFlags.Length; index++)
        {
            ApplyFlag(
                disabledFlags[index],
                enabled: false,
                ref effectiveCaseInsensitive,
                ref effectiveSwapGreed,
                ref effectiveMultiLine,
                ref effectiveDotMatchesNewline,
                ref effectiveCrlf,
                ref effectiveUtf8,
                ref effectiveUnicodeClasses);
        }

        return new RegexCompileOptions(
            effectiveCaseInsensitive,
            effectiveSwapGreed,
            effectiveMultiLine,
            effectiveDotMatchesNewline,
            effectiveCrlf,
            LineTerminator,
            effectiveUtf8,
            effectiveUnicodeClasses,
            SpecializationMode,
            ExcludeLineTerminators,
            ExcludeCrLf,
            ExcludedLineTerminator,
            AllowRawPatternSpecializations);
    }

    /// <summary>
    /// Reports whether a byte is excluded from consuming atoms by line-oriented compilation.
    /// </summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true" /> when the byte is an excluded record terminator.</returns>
    public bool IsExcludedLineTerminator(byte value)
    {
        return ExcludeLineTerminators &&
            (value == ExcludedLineTerminator ||
                ExcludeCrLf && value is (byte)'\r' or (byte)'\n');
    }

    /// <summary>
    /// Creates a copy with the specified line-terminator exclusion policy.
    /// </summary>
    /// <param name="exclude">Whether consuming atoms exclude configured record terminators.</param>
    /// <returns>The updated compilation options.</returns>
    public RegexCompileOptions WithLineTerminatorExclusion(bool exclude)
    {
        return new RegexCompileOptions(
            CaseInsensitive,
            SwapGreed,
            MultiLine,
            DotMatchesNewline,
            Crlf,
            LineTerminator,
            Utf8,
            UnicodeClasses,
            SpecializationMode,
            exclude,
            excludeCrLf: exclude && ExcludeCrLf,
            excludedLineTerminator: ExcludedLineTerminator,
            allowRawPatternSpecializations: AllowRawPatternSpecializations);
    }

    /// <summary>
    /// Creates a copy that matches bytes with ASCII class semantics while preserving all
    /// other compilation policy.
    /// </summary>
    /// <returns>The updated compilation options.</returns>
    public RegexCompileOptions WithAsciiSemantics()
    {
        return new RegexCompileOptions(
            CaseInsensitive,
            SwapGreed,
            MultiLine,
            DotMatchesNewline,
            Crlf,
            LineTerminator,
            utf8: false,
            unicodeClasses: false,
            SpecializationMode,
            ExcludeLineTerminators,
            ExcludeCrLf,
            ExcludedLineTerminator,
            AllowRawPatternSpecializations);
    }

    /// <summary>
    /// Creates a copy that prevents specializations from rescanning the original pattern bytes.
    /// </summary>
    /// <returns>The updated compilation options.</returns>
    public RegexCompileOptions WithoutRawPatternSpecializations()
    {
        return new RegexCompileOptions(
            CaseInsensitive,
            SwapGreed,
            MultiLine,
            DotMatchesNewline,
            Crlf,
            LineTerminator,
            Utf8,
            UnicodeClasses,
            SpecializationMode,
            ExcludeLineTerminators,
            ExcludeCrLf,
            ExcludedLineTerminator,
            allowRawPatternSpecializations: false);
    }

    private static void ApplyFlag(
        char flag,
        bool enabled,
        ref bool caseInsensitive,
        ref bool swapGreed,
        ref bool multiLine,
        ref bool dotMatchesNewline,
        ref bool crlf,
        ref bool utf8,
        ref bool unicodeClasses)
    {
        switch (flag)
        {
            case 'i':
                caseInsensitive = enabled;
                break;
            case 'm':
                multiLine = enabled;
                break;
            case 's':
                dotMatchesNewline = enabled;
                break;
            case 'U':
                swapGreed = enabled;
                break;
            case 'R':
                crlf = enabled;
                break;
            case 'u':
                utf8 = enabled;
                unicodeClasses = enabled;
                break;
        }
    }
}
