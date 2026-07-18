namespace Scout;

/// <summary>
/// Identifies whether a line candidate still requires authoritative regex verification.
/// </summary>
internal enum RegexLineCandidateKind
{
    /// <summary>
    /// The candidate may contain a match and must be verified by the authoritative matcher.
    /// </summary>
    Possible,

    /// <summary>
    /// The authoritative matcher has confirmed that the candidate line contains a match.
    /// </summary>
    Confirmed,
}
