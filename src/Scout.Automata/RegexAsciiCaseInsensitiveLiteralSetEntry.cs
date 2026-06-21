namespace Scout;

internal readonly struct RegexAsciiCaseInsensitiveLiteralSetEntry(
    int literalId,
    byte[] literal,
    int anchorIndex)
{
    public int LiteralId { get; } = literalId;

    public byte[] Literal { get; } = literal;

    public int AnchorIndex { get; } = anchorIndex;
}
