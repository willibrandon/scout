using System.Text;

namespace Scout;

/// <summary>
/// Stores a UTF-8 byte trie or a pre-minimized immutable compile plan.
/// </summary>
/// <param name="compilePlan">The optional pre-minimized compile plan.</param>
internal sealed class RegexUtf8ByteTrie(RegexUtf8ByteTriePlan? compilePlan = null)
{
    private readonly RegexUtf8ByteTrieNode _root = new();
    private RegexUtf8ByteTriePlan? _compilePlan = compilePlan;

    /// <summary>
    /// Gets a value indicating whether the trie and compile plan are empty.
    /// </summary>
    public bool IsEmpty => _compilePlan is null && _root.Children.Count == 0;

    /// <summary>
    /// Adds one scalar's UTF-8 encoding to the materialized trie.
    /// </summary>
    /// <param name="scalar">The Unicode scalar value.</param>
    /// <param name="reversed">Whether to add the encoding in reverse byte order.</param>
    public void AddScalar(int scalar, bool reversed)
    {
        if (_compilePlan is not null)
        {
            throw new InvalidOperationException("Scalars cannot be added to a pre-minimized UTF-8 compile plan.");
        }

        Span<byte> buffer = stackalloc byte[4];
        int length = new Rune(scalar).EncodeToUtf8(buffer);
        RegexUtf8ByteTrieNode current = _root;
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

    /// <summary>
    /// Lowers the reusable trie plan into NFA states with a caller-provided continuation.
    /// </summary>
    /// <param name="next">The continuation state.</param>
    /// <param name="addSplit">Adds an ordered split state.</param>
    /// <param name="addSparse">Adds an ordered sparse-transition state.</param>
    /// <returns>The start state of the lowered trie.</returns>
    public int Compile(int next, RegexAddSplitState addSplit, RegexAddSparseState addSparse)
    {
        _compilePlan ??= RegexUtf8ByteTriePlan.Create(_root);
        return _compilePlan.Compile(next, addSplit, addSparse);
    }
}
