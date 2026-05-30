namespace Scout;

internal readonly struct JsonMatchSpan
{
    public JsonMatchSpan(int start, int end, byte[]? replacement)
    {
        Start = start;
        End = end;
        Replacement = replacement;
    }

    public int Start { get; }

    public int End { get; }

    public byte[]? Replacement { get; }
}
