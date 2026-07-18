namespace Scout;

/// <summary>
/// Searches an ordered exact-literal set through a compiler-proven common prefix.
/// </summary>
/// <param name="literals">The ordered immutable literal bytes.</param>
/// <param name="prefixFinder">The reusable finder for the common prefix.</param>
/// <param name="prefixLiteralIds">The source-ordered literals equal to the common prefix.</param>
/// <param name="literalIdsByNextByte">The source-ordered continuing literals indexed by their next byte.</param>
internal sealed class RegexCommonPrefixLiteralSetScanner(
    byte[][] literals,
    MemmemFinder prefixFinder,
    int[] prefixLiteralIds,
    int[][] literalIdsByNextByte)
{
    private const int MinimumLiteralCount = 16;
    private const int MinimumPrefixLength = 8;

    private readonly byte[][] _literals = literals;
    private readonly MemmemFinder _prefixFinder = prefixFinder;
    private readonly int[] _prefixLiteralIds = prefixLiteralIds;
    private readonly int[][] _literalIdsByNextByte = literalIdsByNextByte;
    private readonly bool _prefixContainsNul =
        prefixFinder.Needle.Span.Contains((byte)0);

    /// <summary>
    /// Creates a scanner when every literal shares a sufficiently selective prefix.
    /// </summary>
    /// <param name="literals">The ordered exact literals.</param>
    /// <param name="scanner">Receives the common-prefix scanner.</param>
    /// <param name="takeLiteralOwnership">Whether the caller guarantees that the supplied literal arrays remain immutable.</param>
    /// <returns><see langword="true" /> when the literal set has a selective common prefix.</returns>
    internal static bool TryCreate(
        IReadOnlyList<byte[]> literals,
        out RegexCommonPrefixLiteralSetScanner? scanner,
        bool takeLiteralOwnership = false)
    {
        ArgumentNullException.ThrowIfNull(literals);
        scanner = null;
        if (!TryGetCommonPrefixLength(literals, out int prefixLength))
        {
            return false;
        }

        byte[] first = literals[0];

        byte[][] ownedLiterals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            ownedLiterals[index] = takeLiteralOwnership
                ? literals[index]
                : literals[index].ToArray();
        }

        BuildCandidateIndexes(
            ownedLiterals,
            prefixLength,
            out int[] prefixLiteralIds,
            out int[][] literalIdsByNextByte);
        scanner = new RegexCommonPrefixLiteralSetScanner(
            ownedLiterals,
            new MemmemFinder(first.AsSpan(0, prefixLength)),
            prefixLiteralIds,
            literalIdsByNextByte);
        return true;
    }

    /// <summary>
    /// Determines whether an exact literal set has a sufficiently selective common prefix.
    /// </summary>
    /// <param name="literals">The ordered exact literals.</param>
    /// <returns><see langword="true" /> when the common-prefix scanner can be constructed.</returns>
    internal static bool CanCreate(IReadOnlyList<byte[]> literals)
    {
        ArgumentNullException.ThrowIfNull(literals);
        return TryGetCommonPrefixLength(literals, out _);
    }

    /// <summary>
    /// Gets the number of literals verified after a common-prefix occurrence followed by a byte.
    /// </summary>
    /// <param name="nextByte">The byte immediately following the common prefix.</param>
    /// <returns>The number of source-ordered verification candidates.</returns>
    internal int GetVerificationCandidateCount(byte nextByte)
    {
        return _prefixLiteralIds.Length + _literalIdsByNextByte[nextByte].Length;
    }

    /// <summary>
    /// Finds the leftmost match, breaking equal-start ties by literal order.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The selected literal and match, or <see langword="null" />.</returns>
    internal RegexLiteralSetCandidate? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset <= haystack.Length - _prefixFinder.Needle.Length)
        {
            int relative = _prefixFinder.Find(haystack[offset..]);
            if (relative < 0)
            {
                return null;
            }

            int candidateStart = offset + relative;
            int nextByteOffset = candidateStart + _prefixFinder.Needle.Length;
            ReadOnlySpan<int> continuingLiteralIds = nextByteOffset < haystack.Length
                ? _literalIdsByNextByte[haystack[nextByteOffset]]
                : [];
            if (TryFindAt(
                    haystack,
                    candidateStart,
                    continuingLiteralIds,
                    out RegexLiteralSetCandidate candidate))
            {
                return candidate;
            }

            offset = candidateStart + 1;
        }

        return null;
    }

    /// <summary>
    /// Counts non-overlapping matches at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The match count.</returns>
    internal long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    /// <summary>
    /// Attempts to count non-overlapping matches while the common-prefix search observes NUL bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="count">Receives the non-overlapping match count.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> after one complete prefix scan produces both results.</returns>
    internal bool TryCountMatchesAndDetectNul(
        ReadOnlySpan<byte> haystack,
        out long count,
        out bool containsNul)
    {
        count = 0;
        containsNul = false;
        int offset = 0;
        while (offset <= haystack.Length)
        {
            int relative = _prefixFinder.FindAndDetectNul(
                haystack[offset..],
                ref containsNul);
            if (relative < 0)
            {
                return true;
            }

            int candidateStart = offset + relative;
            containsNul |= _prefixContainsNul;
            int nextByteOffset = candidateStart + _prefixFinder.Needle.Length;
            ReadOnlySpan<int> continuingLiteralIds = nextByteOffset < haystack.Length
                ? _literalIdsByNextByte[haystack[nextByteOffset]]
                : [];
            if (TryFindAt(
                    haystack,
                    candidateStart,
                    continuingLiteralIds,
                    out RegexLiteralSetCandidate candidate))
            {
                count++;
                containsNul |= _literals[candidate.LiteralId]
                    .AsSpan()
                    .Contains((byte)0);
                offset = candidate.Match.End;
            }
            else
            {
                offset = candidateStart + 1;
            }
        }

        return true;
    }

    /// <summary>
    /// Sums non-overlapping match lengths at or after a byte offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The sum of match lengths.</returns>
    internal long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long total = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (offset <= haystack.Length)
        {
            RegexLiteralSetCandidate? candidate = Find(haystack, offset);
            if (!candidate.HasValue)
            {
                return total;
            }

            RegexMatch match = candidate.Value.Match;
            total += sumSpans ? match.Length : 1;
            offset = match.End;
        }

        return total;
    }

    private static void BuildCandidateIndexes(
        byte[][] literals,
        int prefixLength,
        out int[] prefixLiteralIds,
        out int[][] literalIdsByNextByte)
    {
        var prefixIds = new List<int>();
        var idsByNextByte = new List<int>?[byte.MaxValue + 1];
        for (int literalId = 0; literalId < literals.Length; literalId++)
        {
            byte[] literal = literals[literalId];
            if (literal.Length == prefixLength)
            {
                prefixIds.Add(literalId);
                continue;
            }

            byte nextByte = literal[prefixLength];
            (idsByNextByte[nextByte] ??= []).Add(literalId);
        }

        prefixLiteralIds = prefixIds.ToArray();
        literalIdsByNextByte = new int[idsByNextByte.Length][];
        for (int index = 0; index < idsByNextByte.Length; index++)
        {
            literalIdsByNextByte[index] = idsByNextByte[index]?.ToArray() ?? [];
        }
    }

    private bool TryFindAt(
        ReadOnlySpan<byte> haystack,
        int candidateStart,
        ReadOnlySpan<int> continuingLiteralIds,
        out RegexLiteralSetCandidate candidate)
    {
        int prefixIndex = 0;
        int continuingIndex = 0;
        while (prefixIndex < _prefixLiteralIds.Length ||
            continuingIndex < continuingLiteralIds.Length)
        {
            int literalId;
            if (continuingIndex >= continuingLiteralIds.Length ||
                (prefixIndex < _prefixLiteralIds.Length &&
                    _prefixLiteralIds[prefixIndex] < continuingLiteralIds[continuingIndex]))
            {
                literalId = _prefixLiteralIds[prefixIndex++];
            }
            else
            {
                literalId = continuingLiteralIds[continuingIndex++];
            }

            byte[] literal = _literals[literalId];
            if (literal.Length <= haystack.Length - candidateStart &&
                haystack.Slice(candidateStart, literal.Length).SequenceEqual(literal))
            {
                candidate = new RegexLiteralSetCandidate(
                    literalId,
                    new RegexMatch(candidateStart, literal.Length));
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static int CommonPrefixLength(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        int maximumLength)
    {
        int length = Math.Min(maximumLength, Math.Min(left.Length, right.Length));
        int index = 0;
        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static bool TryGetCommonPrefixLength(
        IReadOnlyList<byte[]> literals,
        out int prefixLength)
    {
        prefixLength = 0;
        if (literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        byte[] first = literals[0] ?? throw new ArgumentNullException(nameof(literals));
        prefixLength = first.Length;
        for (int index = 1; index < literals.Count; index++)
        {
            byte[] literal = literals[index] ?? throw new ArgumentNullException(nameof(literals));
            if (prefixLength >= MinimumPrefixLength)
            {
                prefixLength = CommonPrefixLength(first, literal, prefixLength);
            }
        }

        return prefixLength >= MinimumPrefixLength;
    }
}
