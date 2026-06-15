namespace Scout;

internal readonly struct RegexAnchoredLineLiteralGapPart
{
    private RegexAnchoredLineLiteralGapPart(
        RegexAnchoredLineLiteralGapPartKind kind,
        byte[]? literal,
        bool[]? bytes,
        bool lazy)
    {
        Kind = kind;
        Literal = literal;
        Bytes = bytes;
        Lazy = lazy;
    }

    public RegexAnchoredLineLiteralGapPartKind Kind { get; }

    public byte[]? Literal { get; }

    public bool[]? Bytes { get; }

    public bool Lazy { get; }

    public static RegexAnchoredLineLiteralGapPart CreateLiteral(byte[] literal)
    {
        return new RegexAnchoredLineLiteralGapPart(RegexAnchoredLineLiteralGapPartKind.Literal, literal, null, lazy: false);
    }

    public static RegexAnchoredLineLiteralGapPart CreateByteSet(bool[] bytes)
    {
        return new RegexAnchoredLineLiteralGapPart(RegexAnchoredLineLiteralGapPartKind.ByteSet, null, bytes, lazy: false);
    }

    public static RegexAnchoredLineLiteralGapPart CreateOptionalByteSet(bool[] bytes, bool lazy)
    {
        return new RegexAnchoredLineLiteralGapPart(RegexAnchoredLineLiteralGapPartKind.OptionalByteSet, null, bytes, lazy);
    }

    public static RegexAnchoredLineLiteralGapPart CreateStarByteSet(bool[] bytes, bool lazy)
    {
        return new RegexAnchoredLineLiteralGapPart(RegexAnchoredLineLiteralGapPartKind.StarByteSet, null, bytes, lazy);
    }
}
