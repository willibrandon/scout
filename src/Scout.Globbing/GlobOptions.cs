using System;

namespace Scout;

/// <summary>
/// Configures byte-oriented glob matching.
/// </summary>
public sealed class GlobOptions
{
    private readonly byte[] separators;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobOptions" /> class.
    /// </summary>
    /// <param name="literalSeparator">Whether wildcards are prevented from matching path separators.</param>
    /// <param name="backslashEscapes">Whether backslash escapes glob metacharacters.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII byte case is ignored.</param>
    /// <param name="pathSeparators">The bytes treated as path separators.</param>
    /// <param name="matchBaseName">Whether separator-free patterns can match only the final path component.</param>
    /// <param name="emptyAlternates">Whether empty brace alternatives are accepted.</param>
    /// <param name="allowUnclosedClass">Whether unclosed character classes are treated as literals.</param>
    public GlobOptions(
        bool literalSeparator = false,
        bool backslashEscapes = true,
        bool asciiCaseInsensitive = false,
        byte[]? pathSeparators = null,
        bool matchBaseName = false,
        bool emptyAlternates = false,
        bool allowUnclosedClass = false)
    {
        LiteralSeparator = literalSeparator;
        BackslashEscapes = backslashEscapes;
        AsciiCaseInsensitive = asciiCaseInsensitive;
        MatchBaseName = matchBaseName;
        EmptyAlternates = emptyAlternates;
        AllowUnclosedClass = allowUnclosedClass;
        separators = pathSeparators is null || pathSeparators.Length == 0
            ? [(byte)'/']
            : pathSeparators.AsSpan().ToArray();
    }

    /// <summary>
    /// Gets default Unix-style glob options.
    /// </summary>
    public static GlobOptions Unix { get; } = new();

    /// <summary>
    /// Gets Unix-style ignore/type glob options where wildcards cannot match separators.
    /// </summary>
    public static GlobOptions UnixLiteralSeparator { get; } = new(literalSeparator: true);

    /// <summary>
    /// Gets Windows-style glob options.
    /// </summary>
    public static GlobOptions Windows { get; } = new(backslashEscapes: false, pathSeparators: [(byte)'/', (byte)'\\']);

    /// <summary>
    /// Gets Windows-style ignore/type glob options where wildcards cannot match separators.
    /// </summary>
    public static GlobOptions WindowsLiteralSeparator { get; } = new(literalSeparator: true, backslashEscapes: false, pathSeparators: [(byte)'/', (byte)'\\']);

    /// <summary>
    /// Gets a value indicating whether wildcards are prevented from matching path separators.
    /// </summary>
    public bool LiteralSeparator { get; }

    /// <summary>
    /// Gets a value indicating whether backslash escapes glob metacharacters.
    /// </summary>
    public bool BackslashEscapes { get; }

    /// <summary>
    /// Gets a value indicating whether ASCII byte case is ignored.
    /// </summary>
    public bool AsciiCaseInsensitive { get; }

    /// <summary>
    /// Gets a value indicating whether separator-free patterns can match only the final path component.
    /// </summary>
    public bool MatchBaseName { get; }

    /// <summary>
    /// Gets a value indicating whether empty brace alternatives are accepted.
    /// </summary>
    public bool EmptyAlternates { get; }

    /// <summary>
    /// Gets a value indicating whether unclosed character classes are treated as literals.
    /// </summary>
    public bool AllowUnclosedClass { get; }

    internal ReadOnlySpan<byte> PathSeparators => separators;
}
