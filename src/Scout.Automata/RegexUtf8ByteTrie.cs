using System.Text;

namespace Scout;

internal sealed class RegexUtf8ByteTrie
{
    private readonly RegexUtf8ByteTrieNode root = new();
    private RegexUtf8ByteTriePlan? compilePlan;

    public bool IsEmpty => root.Children.Count == 0;

    public void AddScalar(int scalar, bool reversed)
    {
        Span<byte> buffer = stackalloc byte[4];
        int length = new Rune(scalar).EncodeToUtf8(buffer);
        RegexUtf8ByteTrieNode current = root;
        if (reversed)
        {
            for (int index = length - 1; index >= 0; index--)
            {
                current = current.GetOrAdd(buffer[index]);
            }
        }
        else
        {
            for (int index = 0; index < length; index++)
            {
                current = current.GetOrAdd(buffer[index]);
            }
        }

        current.Accept = true;
    }

    public int Compile(int next, RegexAddSplitState addSplit, RegexAddSparseState addSparse)
    {
        compilePlan ??= RegexUtf8ByteTriePlan.Create(root);
        return compilePlan.Compile(next, addSplit, addSparse);
    }
}
