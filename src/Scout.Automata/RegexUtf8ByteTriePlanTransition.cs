namespace Scout;

internal sealed class RegexUtf8ByteTriePlanTransition
{
    public RegexUtf8ByteTriePlanTransition(byte start, byte end, RegexUtf8ByteTriePlan target)
    {
        Start = start;
        End = end;
        Target = target;
        Ranges = [start, end];
    }

    public byte Start { get; }

    public byte End { get; }

    public byte[] Ranges { get; }

    public RegexUtf8ByteTriePlan Target { get; }
}
