namespace Scout;

internal readonly struct RegexFixedWidthLiteralSeed
{
    public RegexFixedWidthLiteralSeed(int alternativeIndex, int offset, byte[] literal)
    {
        AlternativeIndex = alternativeIndex;
        Offset = offset;
        Literal = literal;
    }

    public int AlternativeIndex { get; }

    public int Offset { get; }

    public byte[] Literal { get; }
}
