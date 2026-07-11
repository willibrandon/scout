using System.Text;

namespace Scout;

/// <summary>
/// Represents an immutable, reusable DAG for lowering UTF-8 byte sequences into NFA states.
/// </summary>
/// <param name="id">The plan-local node identifier.</param>
/// <param name="accept">Whether this node accepts by continuing at the caller's target.</param>
/// <param name="transitions">The ordered byte-range transitions.</param>
internal sealed class RegexUtf8ByteTriePlan(
    int id,
    bool accept,
    RegexUtf8ByteTriePlanTransition[] transitions)
{
    private readonly int _id = id;
    private readonly bool _accept = accept;
    private readonly RegexUtf8ByteTriePlanTransition[] _transitions = transitions;

    internal int Id => _id;

    /// <summary>
    /// Creates a minimized compile plan from a materialized byte trie.
    /// </summary>
    /// <param name="root">The byte-trie root.</param>
    /// <returns>The immutable compile-plan root.</returns>
    public static RegexUtf8ByteTriePlan Create(RegexUtf8ByteTrieNode root)
    {
        Dictionary<string, RegexUtf8ByteTriePlan> interned = new(StringComparer.Ordinal);
        int nextId = 0;
        return CreateNode(root, interned, ref nextId);
    }

    /// <summary>
    /// Lowers this reusable plan into NFA states that continue at a caller-provided target.
    /// </summary>
    /// <param name="next">The continuation state.</param>
    /// <param name="addSplit">Adds an ordered split state.</param>
    /// <param name="addSparse">Adds an ordered sparse-transition state.</param>
    /// <returns>The start state of the lowered plan.</returns>
    public int Compile(int next, RegexAddSplitState addSplit, RegexAddSparseState addSparse)
    {
        Dictionary<(int Plan, int Next), int> memo = [];
        return Compile(next, addSplit, addSparse, memo);
    }

    internal bool HasTransitions(ReadOnlySpan<RegexUtf8ByteTriePlanTransition> transitions)
    {
        if (_accept || transitions.Length != _transitions.Length)
        {
            return false;
        }

        for (int index = 0; index < transitions.Length; index++)
        {
            RegexUtf8ByteTriePlanTransition left = _transitions[index];
            RegexUtf8ByteTriePlanTransition right = transitions[index];
            if (left.Start != right.Start ||
                left.End != right.End ||
                !ReferenceEquals(left.Target, right.Target))
            {
                return false;
            }
        }

        return true;
    }

    private int Compile(
        int next,
        RegexAddSplitState addSplit,
        RegexAddSparseState addSparse,
        Dictionary<(int Plan, int Next), int> memo)
    {
        if (_accept && _transitions.Length == 0)
        {
            return next;
        }

        (int Plan, int Next) key = (_id, next);
        if (memo.TryGetValue(key, out int existing))
        {
            return existing;
        }

        List<int> branches = new(2);
        if (_accept)
        {
            branches.Add(next);
        }

        if (_transitions.Length > 0)
        {
            var sparseTransitions = new RegexNfaSparseTransition[_transitions.Length];
            for (int index = 0; index < _transitions.Length; index++)
            {
                RegexUtf8ByteTriePlanTransition transition = _transitions[index];
                int target = transition.Target.Compile(next, addSplit, addSparse, memo);
                sparseTransitions[index] = new RegexNfaSparseTransition(
                    transition.Start,
                    transition.End,
                    target);
            }

            branches.Add(addSparse(sparseTransitions));
        }

        int start = branches[^1];
        for (int index = branches.Count - 2; index >= 0; index--)
        {
            start = addSplit(branches[index], start);
        }

        memo.Add(key, start);
        return start;
    }

    private static RegexUtf8ByteTriePlan CreateNode(
        RegexUtf8ByteTrieNode node,
        Dictionary<string, RegexUtf8ByteTriePlan> interned,
        ref int nextId)
    {
        List<(byte Value, RegexUtf8ByteTriePlan Target)> children = new(node.Children.Count);
        foreach (KeyValuePair<byte, RegexUtf8ByteTrieNode> child in node.Children)
        {
            children.Add((child.Key, CreateNode(child.Value, interned, ref nextId)));
        }

        children.Sort(static (left, right) => left.Value.CompareTo(right.Value));
        RegexUtf8ByteTriePlanTransition[] transitions = CreateTransitions(children);
        string key = BuildKey(node.Accept, transitions);
        if (interned.TryGetValue(key, out RegexUtf8ByteTriePlan? existing))
        {
            return existing;
        }

        RegexUtf8ByteTriePlan created = new(nextId++, node.Accept, transitions);
        interned.Add(key, created);
        return created;
    }

    private static RegexUtf8ByteTriePlanTransition[] CreateTransitions(
        List<(byte Value, RegexUtf8ByteTriePlan Target)> children)
    {
        if (children.Count == 0)
        {
            return [];
        }

        List<RegexUtf8ByteTriePlanTransition> transitions = [];
        byte start = children[0].Value;
        byte end = start;
        RegexUtf8ByteTriePlan target = children[0].Target;
        for (int index = 1; index < children.Count; index++)
        {
            (byte value, RegexUtf8ByteTriePlan childTarget) = children[index];
            if (value == end + 1 && ReferenceEquals(childTarget, target))
            {
                end = value;
                continue;
            }

            transitions.Add(new RegexUtf8ByteTriePlanTransition(start, end, target));
            start = value;
            end = value;
            target = childTarget;
        }

        transitions.Add(new RegexUtf8ByteTriePlanTransition(start, end, target));
        return transitions.ToArray();
    }

    private static string BuildKey(bool accept, RegexUtf8ByteTriePlanTransition[] transitions)
    {
        StringBuilder builder = new(1 + transitions.Length * 12);
        builder.Append(accept ? '1' : '0');
        builder.Append(';');
        for (int index = 0; index < transitions.Length; index++)
        {
            RegexUtf8ByteTriePlanTransition transition = transitions[index];
            builder.Append(transition.Start);
            builder.Append('-');
            builder.Append(transition.End);
            builder.Append(':');
            builder.Append(transition.Target._id);
            builder.Append(';');
        }

        return builder.ToString();
    }
}
