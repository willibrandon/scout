namespace Scout;

internal readonly struct RegexBoundedLineLiteralGapBranch(byte[] prefix, byte[] suffix, int maximumRuns)
{
    public byte[] Prefix { get; } = prefix;

    public byte[] Suffix { get; } = suffix;

    public int MaximumRuns { get; } = maximumRuns;
}
