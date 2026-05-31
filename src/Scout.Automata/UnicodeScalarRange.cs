namespace Scout;

internal readonly struct UnicodeScalarRange
{
    internal UnicodeScalarRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    internal int Start { get; }

    internal int End { get; }
}
