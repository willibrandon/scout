namespace Scout;

/// <summary>
/// Describes a position that identifies a possible or confirmed matching line.
/// </summary>
/// <param name="offset">A byte offset contained by the candidate line.</param>
/// <param name="kind">Whether the candidate requires authoritative verification.</param>
internal readonly struct RegexLineCandidate(int offset, RegexLineCandidateKind kind)
{
    /// <summary>
    /// Gets a byte offset contained by the candidate line.
    /// </summary>
    internal int Offset { get; } = offset;

    /// <summary>
    /// Gets whether the candidate requires authoritative verification.
    /// </summary>
    internal RegexLineCandidateKind Kind { get; } = kind;
}
