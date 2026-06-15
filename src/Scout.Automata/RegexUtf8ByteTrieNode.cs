namespace Scout;

internal sealed class RegexUtf8ByteTrieNode
{
    public Dictionary<byte, RegexUtf8ByteTrieNode> Children { get; } = [];

    public bool Accept { get; set; }

    public RegexUtf8ByteTrieNode GetOrAdd(byte value)
    {
        if (Children.TryGetValue(value, out RegexUtf8ByteTrieNode? existing))
        {
            return existing;
        }

        RegexUtf8ByteTrieNode created = new();
        Children.Add(value, created);
        return created;
    }
}
