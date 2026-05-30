namespace Scout;

internal sealed class AhoCorasickOutput
{
    public AhoCorasickOutput(int patternId, int length)
    {
        PatternId = patternId;
        Length = length;
    }

    public int PatternId { get; }

    public int Length { get; }
}
