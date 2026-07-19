namespace Scout;

/// <summary>
/// Identifies one node in an authoritative bracketed character-class expression.
/// </summary>
internal enum RegexClassSetKind
{
    /// <summary>
    /// Represents an empty scalar set.
    /// </summary>
    Empty,

    /// <summary>
    /// Represents one literal scalar.
    /// </summary>
    Literal,

    /// <summary>
    /// Represents an inclusive scalar range.
    /// </summary>
    Range,

    /// <summary>
    /// Represents a shorthand or Unicode property atom.
    /// </summary>
    Atom,

    /// <summary>
    /// Represents a nested bracketed expression.
    /// </summary>
    Bracketed,

    /// <summary>
    /// Represents the union of adjacent class items.
    /// </summary>
    Union,

    /// <summary>
    /// Represents a binary set operation.
    /// </summary>
    Binary,
}
