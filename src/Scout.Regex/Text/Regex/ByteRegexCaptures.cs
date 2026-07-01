namespace Scout.Text.Regex;

/// <summary>
/// Describes a byte regex match and the capture groups that participated in it.
/// </summary>
public sealed class ByteRegexCaptures
{
    private readonly RegexCaptures captures;

    internal ByteRegexCaptures(RegexCaptures captures)
    {
        this.captures = captures;
    }

    /// <summary>
    /// Gets the overall match.
    /// </summary>
    public ByteRegexMatch Match => ByteRegexMatch.FromRegexMatch(captures.Match);

    /// <summary>
    /// Gets the number of capture slots, including slot zero for the whole match.
    /// </summary>
    public int GroupCount => captures.GroupCount;

    /// <summary>
    /// Gets the capture group for a slot, or <see langword="null" /> when it did not participate.
    /// </summary>
    /// <param name="index">The zero-based capture slot.</param>
    /// <returns>The capture span, or <see langword="null" />.</returns>
    public ByteRegexMatch? GetGroup(int index)
    {
        RegexMatch? group = captures.GetGroup(index);
        return group.HasValue ? ByteRegexMatch.FromRegexMatch(group.Value) : null;
    }

    /// <summary>
    /// Counts capture slots that participated in the match.
    /// </summary>
    /// <returns>The participating capture count.</returns>
    public int ParticipatingCount()
    {
        return captures.ParticipatingCount();
    }
}
