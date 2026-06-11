namespace Scout;

internal sealed class RegexDelimitedCaptureField
{
    private readonly bool[] matches;

    public RegexDelimitedCaptureField(int captureIndex, int minimum, int? maximum, bool[] matches)
    {
        CaptureIndex = captureIndex;
        Minimum = minimum;
        Maximum = maximum;
        this.matches = matches;
    }

    public int CaptureIndex { get; }

    public int Minimum { get; }

    public int? Maximum { get; }

    public bool Matches(byte value)
    {
        return matches[value];
    }
}
