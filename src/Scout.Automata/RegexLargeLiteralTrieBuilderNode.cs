namespace Scout;

internal sealed class RegexLargeLiteralTrieBuilderNode
{
    public Dictionary<byte, int> Transitions { get; } = [];

    public int BestLiteralId { get; set; } = -1;

    public int BestLiteralLength { get; set; }
}
