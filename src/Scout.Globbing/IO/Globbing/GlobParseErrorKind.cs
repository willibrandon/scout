namespace Scout.IO.Globbing;

/// <summary>
/// Identifies the kind of glob parse error.
/// </summary>
public enum GlobParseErrorKind
{
    /// <summary>
    /// The parse error kind is not specified.
    /// </summary>
    Unknown,

    /// <summary>
    /// A character class is missing its closing bracket.
    /// </summary>
    UnclosedClass,

    /// <summary>
    /// A character class range has an end byte lower than its start byte.
    /// </summary>
    InvalidRange,

    /// <summary>
    /// An alternate group close brace appears without a matching open brace.
    /// </summary>
    UnopenedAlternates,

    /// <summary>
    /// An alternate group open brace is missing its closing brace.
    /// </summary>
    UnclosedAlternates,

    /// <summary>
    /// A trailing backslash escape has no escaped byte.
    /// </summary>
    DanglingEscape,
}
