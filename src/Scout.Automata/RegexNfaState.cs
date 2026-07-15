namespace Scout;

/// <summary>
/// Represents one compiled Thompson NFA state and its effective matching options.
/// </summary>
/// <param name="kind">The state operation.</param>
/// <param name="atomKind">The syntax kind consumed or asserted by the state.</param>
/// <param name="value">The atom payload.</param>
/// <param name="caseInsensitive">Whether the atom ignores case.</param>
/// <param name="multiLine">Whether anchors use multiline semantics.</param>
/// <param name="dotMatchesNewline">Whether dot normally consumes line terminators.</param>
/// <param name="crlf">Whether CR and LF form the line-terminator family.</param>
/// <param name="lineTerminator">The configured non-CRLF line terminator.</param>
/// <param name="utf8">Whether the state observes UTF-8 scalar boundaries.</param>
/// <param name="unicodeClasses">Whether character classes use Unicode semantics.</param>
/// <param name="next">The primary successor state.</param>
/// <param name="alternative">The alternative successor state.</param>
/// <param name="captureIndex">The capture slot manipulated by a capture state.</param>
/// <param name="sparseTransitions">The byte ranges and targets for a sparse state.</param>
/// <param name="excludeLineTerminators">Whether consuming atoms exclude configured record terminators.</param>
/// <param name="excludeCrLf">Whether exclusion treats CR and LF as one record-terminator family.</param>
/// <param name="excludedLineTerminator">The record byte excluded from consuming atoms, or <see langword="null" /> to use <paramref name="lineTerminator" />.</param>
internal sealed class RegexNfaState(
    RegexNfaStateKind kind,
    RegexSyntaxKind atomKind,
    ReadOnlyMemory<byte> value,
    bool caseInsensitive,
    bool multiLine,
    bool dotMatchesNewline,
    bool crlf,
    byte lineTerminator,
    bool utf8,
    bool unicodeClasses,
    int next,
    int alternative,
    int captureIndex = 0,
    RegexNfaSparseTransition[]? sparseTransitions = null,
    bool excludeLineTerminators = false,
    bool excludeCrLf = false,
    byte? excludedLineTerminator = null)
{
    /// <summary>
    /// Gets the state operation.
    /// </summary>
    public RegexNfaStateKind Kind { get; } = kind;

    /// <summary>
    /// Gets the syntax kind consumed or asserted by this state.
    /// </summary>
    public RegexSyntaxKind AtomKind { get; } = atomKind;

    /// <summary>
    /// Gets the atom payload.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; } = value;

    /// <summary>
    /// Gets a value indicating whether this atom ignores case.
    /// </summary>
    public bool CaseInsensitive { get; } = caseInsensitive;

    /// <summary>
    /// Gets a value indicating whether anchors use multiline semantics.
    /// </summary>
    public bool MultiLine { get; } = multiLine;

    /// <summary>
    /// Gets a value indicating whether dot normally consumes line terminators.
    /// </summary>
    public bool DotMatchesNewline { get; } = dotMatchesNewline;

    /// <summary>
    /// Gets a value indicating whether CR and LF form the line-terminator family.
    /// </summary>
    public bool Crlf { get; } = crlf;

    /// <summary>
    /// Gets the configured non-CRLF line terminator.
    /// </summary>
    public byte LineTerminator { get; } = lineTerminator;

    /// <summary>
    /// Gets a value indicating whether this state observes UTF-8 scalar boundaries.
    /// </summary>
    public bool Utf8 { get; } = utf8;

    /// <summary>
    /// Gets a value indicating whether character classes use Unicode semantics.
    /// </summary>
    public bool UnicodeClasses { get; } = unicodeClasses;

    /// <summary>
    /// Gets the primary successor state.
    /// </summary>
    public int Next { get; } = next;

    /// <summary>
    /// Gets the alternative successor state.
    /// </summary>
    public int Alternative { get; } = alternative;

    /// <summary>
    /// Gets the capture slot manipulated by this state.
    /// </summary>
    public int CaptureIndex { get; } = captureIndex;

    /// <summary>
    /// Gets the byte ranges and targets for a sparse state.
    /// </summary>
    public RegexNfaSparseTransition[] SparseTransitions { get; } = sparseTransitions ?? [];

    /// <summary>
    /// Gets a value indicating whether consuming atoms exclude configured record terminators.
    /// </summary>
    public bool ExcludeLineTerminators { get; } = excludeLineTerminators;

    /// <summary>
    /// Gets a value indicating whether exclusion treats CR and LF as one record-terminator family.
    /// </summary>
    public bool ExcludeCrLf { get; } = excludeCrLf;

    /// <summary>
    /// Gets the record byte excluded from consuming atoms.
    /// </summary>
    public byte ExcludedLineTerminator { get; } = excludedLineTerminator ?? lineTerminator;

    /// <summary>
    /// Gets a value indicating whether the atom requires scalar decoding.
    /// </summary>
    public bool RequiresUtf8ScalarMatch { get; } = RegexByteClass.RequiresUtf8ScalarMatch(
        atomKind,
        value.Span,
        utf8,
        caseInsensitive,
        unicodeClasses);

    /// <summary>
    /// Gets a value indicating whether ASCII can bypass scalar decoding.
    /// </summary>
    public bool CanUseAsciiScalarFastPath { get; } = RegexByteClass.CanUseAsciiScalarFastPath(
        atomKind,
        value.Span);

    /// <summary>
    /// Reports whether this state's atom matches one byte.
    /// </summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true" /> when the state consumes the byte.</returns>
    public bool AtomMatches(byte value)
    {
        return !IsExcludedLineTerminator(value) &&
            RegexByteClass.AtomMatches(
                value,
                AtomKind,
                Value.Span,
                CaseInsensitive,
                MultiLine,
                DotMatchesNewline,
                Crlf,
                LineTerminator);
    }

    /// <summary>
    /// Attempts to determine the number of bytes consumed by this state's atom.
    /// </summary>
    /// <param name="haystack">The bytes being searched.</param>
    /// <param name="position">The candidate byte position.</param>
    /// <param name="length">Receives the number of consumed bytes.</param>
    /// <returns><see langword="true" /> when the state consumes an atom at the position.</returns>
    public bool TryGetAtomMatchLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        if (position >= haystack.Length || IsExcludedLineTerminator(haystack[position]))
        {
            return false;
        }

        if (AtomKind == RegexSyntaxKind.Literal &&
            !CaseInsensitive &&
            Value.Length == 1)
        {
            if (haystack[position] != Value.Span[0])
            {
                return false;
            }

            length = 1;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            AtomKind,
            Value.Span,
            CaseInsensitive,
            MultiLine,
            DotMatchesNewline,
            Crlf,
            LineTerminator,
            UnicodeClasses,
            RequiresUtf8ScalarMatch,
            CanUseAsciiScalarFastPath,
            out length);
    }

    /// <summary>
    /// Attempts to find a sparse transition for one byte.
    /// </summary>
    /// <param name="value">The byte to consume.</param>
    /// <param name="next">Receives the successor state.</param>
    /// <returns><see langword="true" /> when a transition accepts the byte.</returns>
    public bool TryGetSparseTarget(byte value, out int next)
    {
        if (IsExcludedLineTerminator(value))
        {
            next = -1;
            return false;
        }

        for (int index = 0; index < SparseTransitions.Length; index++)
        {
            RegexNfaSparseTransition transition = SparseTransitions[index];
            if (transition.Start <= value && value <= transition.End)
            {
                next = transition.Next;
                return true;
            }
        }

        next = -1;
        return false;
    }

    private bool IsExcludedLineTerminator(byte value)
    {
        return ExcludeLineTerminators &&
            (value == ExcludedLineTerminator ||
                ExcludeCrLf && value is (byte)'\r' or (byte)'\n');
    }
}
