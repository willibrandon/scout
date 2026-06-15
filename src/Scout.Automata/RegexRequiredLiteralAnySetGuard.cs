using System.Buffers;

namespace Scout;

internal sealed class RegexRequiredLiteralAnySetGuard
{
    private const int MaximumLiteralCount = 32;
    private const int MaximumLiteralLength = 64;
    private const int MaximumDistinctFirstBytes = 16;

    private readonly byte[][] literals;
    private readonly SearchValues<byte> firstBytes;

    private RegexRequiredLiteralAnySetGuard(byte[][] literals)
    {
        this.literals = literals;
        firstBytes = SearchValues.Create(GetDistinctFirstBytes(literals));
    }

    public static RegexRequiredLiteralAnySetGuard? TryCreate(RegexSyntaxNode root, RegexCompileOptions options)
    {
        root = UnwrapTransparentGroups(root, ref options);
        if (root is not RegexAlternationNode alternation || alternation.Alternatives.Count < 2)
        {
            return null;
        }

        var literals = new List<byte[]>();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!RegexPrefilter.TryFindRequiredLiteralCandidate(
                    alternation.Alternatives[index],
                    options,
                    out RegexRequiredLiteralSetCandidate candidate) ||
                candidate.Literals.Length != 1 ||
                !CanUseExactLiteral(candidate.Literals[0], candidate))
            {
                return null;
            }

            byte[] literal = candidate.Literals[0];
            if (literal.Length == 0 ||
                literal.Length > MaximumLiteralLength ||
                !IsSelectiveEnough(literal) ||
                !AddDistinctLiteral(literals, literal) ||
                literals.Count > MaximumLiteralCount)
            {
                return null;
            }
        }

        return literals.Count > 0 && HasAcceptableFirstByteSet(literals)
            ? new RegexRequiredLiteralAnySetGuard(literals.ToArray())
            : null;
    }

    public bool CanSearch(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = startOffset;
        while (searchAt < haystack.Length)
        {
            int offset = haystack[searchAt..].IndexOfAny(firstBytes);
            if (offset < 0)
            {
                return false;
            }

            int position = searchAt + offset;
            if (MatchesAt(haystack, position))
            {
                return true;
            }

            searchAt = position + 1;
        }

        return false;
    }

    private bool MatchesAt(ReadOnlySpan<byte> haystack, int position)
    {
        for (int index = 0; index < literals.Length; index++)
        {
            ReadOnlySpan<byte> literal = literals[index];
            if (literal.Length <= haystack.Length - position &&
                haystack[position..(position + literal.Length)].SequenceEqual(literal))
            {
                return true;
            }
        }

        return false;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node, ref RegexCompileOptions options)
    {
        while (node is RegexGroupNode group)
        {
            options = options.Apply(group.EnabledFlags, group.DisabledFlags);
            node = group.Child;
        }

        return node;
    }

    private static bool CanUseExactLiteral(byte[] literal, RegexRequiredLiteralSetCandidate candidate)
    {
        if (!candidate.CaseInsensitive)
        {
            return true;
        }

        for (int index = 0; index < literal.Length; index++)
        {
            byte value = literal[index];
            if (value >= 0x80 ||
                value is >= (byte)'A' and <= (byte)'Z' ||
                value is >= (byte)'a' and <= (byte)'z')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSelectiveEnough(byte[] literal)
    {
        return literal.Length > 1 || literal[0] is not ((>= (byte)'A' and <= (byte)'Z') or
            (>= (byte)'a' and <= (byte)'z') or
            (>= (byte)'0' and <= (byte)'9') or
            (byte)' ' or
            (byte)'\t' or
            (byte)'\r' or
            (byte)'\n');
    }

    private static bool AddDistinctLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return true;
            }
        }

        literals.Add(literal.ToArray());
        return true;
    }

    private static bool HasAcceptableFirstByteSet(List<byte[]> literals)
    {
        return GetDistinctFirstBytes(literals).Length <= MaximumDistinctFirstBytes;
    }

    private static byte[] GetDistinctFirstBytes(IReadOnlyList<byte[]> literals)
    {
        Span<bool> seen = stackalloc bool[256];
        var bytes = new List<byte>();
        for (int index = 0; index < literals.Count; index++)
        {
            byte value = literals[index][0];
            if (seen[value])
            {
                continue;
            }

            seen[value] = true;
            bytes.Add(value);
        }

        return bytes.ToArray();
    }
}
