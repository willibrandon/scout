namespace Scout;

internal readonly struct PatternSetRequiredLiteral
{
    public PatternSetRequiredLiteral(byte[] literal, int maxLookBehind)
    {
        ArgumentNullException.ThrowIfNull(literal);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLookBehind);

        Literal = literal;
        MaxLookBehind = maxLookBehind;
    }

    public byte[] Literal { get; }

    public int MaxLookBehind { get; }
}
