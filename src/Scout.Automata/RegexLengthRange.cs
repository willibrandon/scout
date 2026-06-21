namespace Scout;

internal readonly record struct RegexLengthRange(int MinimumBytes, int MaximumBytes)
{
    public static RegexLengthRange Zero { get; } = new(0, 0);
}
