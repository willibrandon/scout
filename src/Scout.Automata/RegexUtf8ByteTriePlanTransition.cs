namespace Scout;

/// <summary>
/// Represents one ordered byte-range edge in an immutable UTF-8 compile plan.
/// </summary>
/// <param name="start">The inclusive lower byte.</param>
/// <param name="end">The inclusive upper byte.</param>
/// <param name="target">The target plan node.</param>
internal sealed class RegexUtf8ByteTriePlanTransition(
    byte start,
    byte end,
    RegexUtf8ByteTriePlan target)
{
    /// <summary>
    /// Gets the inclusive lower byte.
    /// </summary>
    public byte Start { get; } = start;

    /// <summary>
    /// Gets the inclusive upper byte.
    /// </summary>
    public byte End { get; } = end;

    /// <summary>
    /// Gets the target plan node.
    /// </summary>
    public RegexUtf8ByteTriePlan Target { get; } = target;
}
