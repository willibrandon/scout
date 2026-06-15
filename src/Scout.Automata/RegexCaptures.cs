namespace Scout;

/// <summary>
/// Describes a regex match and the capture groups that participated in it.
/// </summary>
public sealed class RegexCaptures
{
    private readonly RegexMatch?[] groups;

    internal RegexCaptures(RegexMatch match, RegexMatch?[] groups)
    {
        Match = match;
        this.groups = groups;
    }

    /// <summary>
    /// Gets the overall match.
    /// </summary>
    public RegexMatch Match { get; }

    /// <summary>
    /// Gets the number of capture slots, including slot zero for the whole match.
    /// </summary>
    public int GroupCount => groups.Length;

    /// <summary>
    /// Gets the capture group for a slot, or <see langword="null" /> when it did not participate.
    /// </summary>
    /// <param name="index">The zero-based capture slot.</param>
    /// <returns>The capture span, or <see langword="null" />.</returns>
    public RegexMatch? GetGroup(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, groups.Length);
        return groups[index];
    }

    /// <summary>
    /// Counts capture slots that participated in the match.
    /// </summary>
    /// <returns>The participating capture count.</returns>
    public int ParticipatingCount()
    {
        int count = 0;
        for (int index = 0; index < groups.Length; index++)
        {
            if (groups[index].HasValue)
            {
                count++;
            }
        }

        return count;
    }
}
