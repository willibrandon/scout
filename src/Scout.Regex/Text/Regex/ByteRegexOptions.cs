namespace Scout.Text.Regex;

/// <summary>
/// Configures byte-oriented regex compilation.
/// </summary>
public sealed class ByteRegexOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether ASCII literals and classes match case-insensitively.
    /// </summary>
    public bool AsciiCaseInsensitive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>^</c> and <c>$</c> match next to line terminators.
    /// </summary>
    public bool MultiLine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>.</c> matches line terminators.
    /// </summary>
    public bool DotMatchesNewline { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether CRLF is treated as one line terminator.
    /// </summary>
    public bool Crlf { get; set; }

    /// <summary>
    /// Gets or sets the line terminator byte used when <see cref="Crlf" /> is disabled.
    /// </summary>
    public byte LineTerminator { get; set; } = (byte)'\n';

    /// <summary>
    /// Gets or sets a value indicating whether empty and scalar-consuming matches must respect UTF-8 boundaries.
    /// </summary>
    public bool Utf8 { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Perl classes and word boundaries use Unicode definitions.
    /// </summary>
    public bool UnicodeClasses { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum DFA cache size in bytes, or <see langword="null" /> for Scout's default.
    /// </summary>
    public ulong? DfaSizeLimit { get; set; }

    /// <summary>
    /// Gets or sets the engine family used during compilation.
    /// </summary>
    public ByteRegexEngineMode EngineMode { get; set; } = ByteRegexEngineMode.Optimized;

    internal RegexSpecializationMode ToSpecializationMode()
    {
        return EngineMode switch
        {
            ByteRegexEngineMode.Optimized => RegexSpecializationMode.Default,
            ByteRegexEngineMode.General => RegexSpecializationMode.General,
            ByteRegexEngineMode.AutomataOnly => RegexSpecializationMode.Fallback,
            _ => throw new ArgumentOutOfRangeException(nameof(EngineMode)),
        };
    }
}
