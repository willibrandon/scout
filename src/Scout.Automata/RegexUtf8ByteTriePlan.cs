using System.Text;

namespace Scout;

internal sealed class RegexUtf8ByteTriePlan
{
    private readonly int id;
    private readonly bool accept;
    private readonly RegexUtf8ByteTriePlanTransition[] transitions;

    private RegexUtf8ByteTriePlan(int id, bool accept, RegexUtf8ByteTriePlanTransition[] transitions)
    {
        this.id = id;
        this.accept = accept;
        this.transitions = transitions;
    }

    public static RegexUtf8ByteTriePlan Create(RegexUtf8ByteTrieNode root)
    {
        Dictionary<string, RegexUtf8ByteTriePlan> interned = new(StringComparer.Ordinal);
        int nextId = 0;
        return CreateNode(root, interned, ref nextId);
    }

    public int Compile(int next, RegexAddSplitState addSplit, RegexAddSparseState addSparse)
    {
        Dictionary<(int Plan, int Next), int> memo = [];
        return Compile(next, addSplit, addSparse, memo);
    }

    private int Compile(
        int next,
        RegexAddSplitState addSplit,
        RegexAddSparseState addSparse,
        Dictionary<(int Plan, int Next), int> memo)
    {
        if (accept && transitions.Length == 0)
        {
            return next;
        }

        (int Plan, int Next) key = (id, next);
        if (memo.TryGetValue(key, out int existing))
        {
            return existing;
        }

        List<int> branches = new(2);
        if (accept)
        {
            branches.Add(next);
        }

        if (transitions.Length > 0)
        {
            int sparseCount = 0;
            for (int index = 0; index < transitions.Length; index++)
            {
                sparseCount += transitions[index].Ranges.Length / 2;
            }

            var sparseTransitions = new RegexNfaSparseTransition[sparseCount];
            int sparseIndex = 0;
            for (int index = 0; index < transitions.Length; index++)
            {
                RegexUtf8ByteTriePlanTransition transition = transitions[index];
                int target = transition.Target.Compile(next, addSplit, addSparse, memo);
                for (int rangeIndex = 0; rangeIndex < transition.Ranges.Length; rangeIndex += 2)
                {
                    sparseTransitions[sparseIndex++] = new RegexNfaSparseTransition(
                        transition.Ranges[rangeIndex],
                        transition.Ranges[rangeIndex + 1],
                        target);
                }
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
            builder.Append(transition.Target.id);
            builder.Append(';');
        }

        return builder.ToString();
    }
}
