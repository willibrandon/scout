namespace Scout;

internal readonly struct RegexBoundedLiteralGapBranch(byte[] prefix, byte[] suffix, int minimum, int maximum)
{
    public byte[] Prefix { get; } = prefix;

    public byte[] Suffix { get; } = suffix;

    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;
}
