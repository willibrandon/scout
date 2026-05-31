namespace Scout;

/// <summary>
/// Identifies a parsed regex syntax node.
/// </summary>
public enum RegexSyntaxKind
{
    /// <summary>
    /// Matches the empty string.
    /// </summary>
    Empty,

    /// <summary>
    /// Matches one literal byte.
    /// </summary>
    Literal,

    /// <summary>
    /// Matches any non-line-terminator byte.
    /// </summary>
    Dot,

    /// <summary>
    /// Matches any byte.
    /// </summary>
    AnyClass,

    /// <summary>
    /// Matches a pinned Unicode property class.
    /// </summary>
    UnicodePropertyClass,

    /// <summary>
    /// Matches the negation of a pinned Unicode property class.
    /// </summary>
    NotUnicodePropertyClass,

    /// <summary>
    /// Matches a bracketed character class.
    /// </summary>
    CharacterClass,

    /// <summary>
    /// Matches an ASCII decimal digit.
    /// </summary>
    DigitClass,

    /// <summary>
    /// Matches a byte that is not an ASCII decimal digit.
    /// </summary>
    NotDigitClass,

    /// <summary>
    /// Matches an ASCII word byte.
    /// </summary>
    WordClass,

    /// <summary>
    /// Matches a byte that is not an ASCII word byte.
    /// </summary>
    NotWordClass,

    /// <summary>
    /// Matches regex whitespace.
    /// </summary>
    WhitespaceClass,

    /// <summary>
    /// Matches a byte that is not regex whitespace.
    /// </summary>
    NotWhitespaceClass,

    /// <summary>
    /// Matches an ASCII alphabetic byte.
    /// </summary>
    LetterClass,

    /// <summary>
    /// Matches an ASCII alphabetic or decimal digit byte.
    /// </summary>
    AlphanumericClass,

    /// <summary>
    /// Matches the start anchor.
    /// </summary>
    StartAnchor,

    /// <summary>
    /// Matches the end anchor.
    /// </summary>
    EndAnchor,

    /// <summary>
    /// Matches the absolute start anchor.
    /// </summary>
    AbsoluteStartAnchor,

    /// <summary>
    /// Matches the absolute end anchor.
    /// </summary>
    AbsoluteEndAnchor,

    /// <summary>
    /// Matches a word boundary.
    /// </summary>
    WordBoundary,

    /// <summary>
    /// Matches a non-word-boundary assertion.
    /// </summary>
    NotWordBoundary,

    /// <summary>
    /// Matches a word-start boundary assertion.
    /// </summary>
    WordStartBoundary,

    /// <summary>
    /// Matches a word-end boundary assertion.
    /// </summary>
    WordEndBoundary,

    /// <summary>
    /// Matches the non-word left half of a word-start boundary assertion.
    /// </summary>
    WordStartHalfBoundary,

    /// <summary>
    /// Matches the non-word right half of a word-end boundary assertion.
    /// </summary>
    WordEndHalfBoundary,

    /// <summary>
    /// Concatenates child nodes.
    /// </summary>
    Sequence,

    /// <summary>
    /// Alternates between child nodes.
    /// </summary>
    Alternation,

    /// <summary>
    /// Captures a child expression.
    /// </summary>
    CapturingGroup,

    /// <summary>
    /// Groups a child expression without capturing it.
    /// </summary>
    NonCapturingGroup,

    /// <summary>
    /// Changes inline regex flags.
    /// </summary>
    InlineFlags,

    /// <summary>
    /// Repeats a child expression.
    /// </summary>
    Repetition,
}
