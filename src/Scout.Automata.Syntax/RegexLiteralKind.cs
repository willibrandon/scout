namespace Scout;

/// <summary>
/// Identifies how a literal scalar was written in regex syntax.
/// </summary>
internal enum RegexLiteralKind
{
    /// <summary>
    /// Identifies an unescaped literal scalar.
    /// </summary>
    Verbatim,

    /// <summary>
    /// Identifies an escaped regex metacharacter.
    /// </summary>
    Meta,

    /// <summary>
    /// Identifies permitted superfluous punctuation escaping.
    /// </summary>
    Superfluous,

    /// <summary>
    /// Identifies a named special-character escape.
    /// </summary>
    Special,

    /// <summary>
    /// Identifies a fixed-width hexadecimal escape.
    /// </summary>
    HexFixed,

    /// <summary>
    /// Identifies a braced hexadecimal scalar escape.
    /// </summary>
    HexBrace,

    /// <summary>
    /// Identifies a four-digit Unicode scalar escape.
    /// </summary>
    UnicodeShort,

    /// <summary>
    /// Identifies an eight-digit Unicode scalar escape.
    /// </summary>
    UnicodeLong,
}
