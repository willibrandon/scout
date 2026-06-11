namespace Scout;

internal readonly struct RegexRequiredLiteralSetCandidate
{
    public RegexRequiredLiteralSetCandidate(byte[][] literals, int maxLookBehind)
    {
        Literals = literals;
        MaxLookBehind = maxLookBehind;
    }

    public byte[][] Literals { get; }

    public int MaxLookBehind { get; }
}
