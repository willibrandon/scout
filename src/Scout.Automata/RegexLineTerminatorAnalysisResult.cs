namespace Scout;

/// <summary>
/// Identifies an incompatibility between parsed regex syntax and line-oriented terminator exclusion.
/// </summary>
internal enum RegexLineTerminatorAnalysisResult
{
    /// <summary>
    /// The syntax remains valid after terminator exclusion.
    /// </summary>
    None,

    /// <summary>
    /// The syntax contains an explicit literal record terminator.
    /// </summary>
    ExplicitLiteral,

    /// <summary>
    /// Terminator exclusion empties a consuming atom or character class.
    /// </summary>
    EmptyAtom,
}
