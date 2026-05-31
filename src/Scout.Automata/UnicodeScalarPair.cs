namespace Scout;

internal readonly struct UnicodeScalarPair
{
    internal UnicodeScalarPair(int first, int second)
    {
        First = first;
        Second = second;
    }

    internal int First { get; }

    internal int Second { get; }
}
