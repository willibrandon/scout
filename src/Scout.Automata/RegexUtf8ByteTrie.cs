using System.Text;

namespace Scout;

internal sealed class RegexUtf8ByteTrie
{
    private readonly RegexUtf8ByteTrieNode root = new();

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

    public int Compile(int next, RegexAddByteClassState addByteClass, RegexAddSplitState addSplit)
    {
        Dictionary<string, int> memo = new(StringComparer.Ordinal);
        return CompileNode(root, next, addByteClass, addSplit, memo);
    }

    private static int CompileNode(
        RegexUtf8ByteTrieNode node,
        int next,
        RegexAddByteClassState addByteClass,
        RegexAddSplitState addSplit,
        Dictionary<string, int> memo)
    {
        if (node.Accept && node.Children.Count == 0)
        {
            return next;
        }

        List<(byte Value, int Target)> transitions = new(node.Children.Count);
        foreach (KeyValuePair<byte, RegexUtf8ByteTrieNode> child in node.Children)
        {
            transitions.Add((child.Key, CompileNode(child.Value, next, addByteClass, addSplit, memo)));
        }

        transitions.Sort(static (left, right) => left.Value.CompareTo(right.Value));
        string key = BuildKey(transitions);
        if (memo.TryGetValue(key, out int existing))
        {
            return existing;
        }

        Dictionary<int, List<byte>> targets = [];
        for (int index = 0; index < transitions.Count; index++)
        {
            (byte value, int target) = transitions[index];
            if (!targets.TryGetValue(target, out List<byte>? values))
            {
                values = [];
                targets.Add(target, values);
            }

            values.Add(value);
        }

        List<int> branches = new(targets.Count);
        foreach (KeyValuePair<int, List<byte>> target in targets)
        {
            target.Value.Sort();
            byte[] ranges = ToByteRanges(target.Value);
            branches.Add(addByteClass(ranges, target.Key));
        }

        int start = branches[^1];
        for (int index = branches.Count - 2; index >= 0; index--)
        {
            start = addSplit(branches[index], start);
        }

        memo.Add(key, start);
        return start;
    }

    private static string BuildKey(List<(byte Value, int Target)> transitions)
    {
        StringBuilder builder = new(transitions.Count * 8);
        for (int index = 0; index < transitions.Count; index++)
        {
            builder.Append(transitions[index].Value);
            builder.Append(':');
            builder.Append(transitions[index].Target);
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static byte[] ToByteRanges(List<byte> values)
    {
        List<byte> ranges = [];
        byte start = values[0];
        byte previous = start;
        for (int index = 1; index < values.Count; index++)
        {
            byte value = values[index];
            if (value == previous + 1)
            {
                previous = value;
                continue;
            }

            ranges.Add(start);
            ranges.Add(previous);
            start = value;
            previous = value;
        }

        ranges.Add(start);
        ranges.Add(previous);
        return ranges.ToArray();
    }
}
