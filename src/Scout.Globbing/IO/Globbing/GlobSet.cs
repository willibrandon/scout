
namespace Scout.IO.Globbing;

/// <summary>
/// Represents an ordered set of glob patterns.
/// </summary>
public sealed class GlobSet
{
    private const int StackCandidateLimit = 256;

    private readonly Glob[] globs;
    private readonly byte[][] literalPatterns;
    private readonly int[] literalIndexes;
    private readonly byte[][] basenameLiteralPatterns;
    private readonly int[] basenameLiteralIndexes;
    private readonly byte[][] basenamePrefixPatterns;
    private readonly int[] basenamePrefixIndexes;
    private readonly byte[][] extensionSuffixPatterns;
    private readonly int[] extensionSuffixIndexes;
    private readonly byte[][] requiredExtensionPatterns;
    private readonly int[] requiredExtensionIndexes;
    private readonly AhoCorasickAutomaton? prefixMatcher;
    private readonly int[] prefixIndexes;
    private readonly AhoCorasickAutomaton? suffixMatcher;
    private readonly int[] suffixIndexes;
    private readonly int[] alwaysIndexes;

    private GlobSet(
        Glob[] globs,
        byte[][] literalPatterns,
        int[] literalIndexes,
        byte[][] basenameLiteralPatterns,
        int[] basenameLiteralIndexes,
        byte[][] basenamePrefixPatterns,
        int[] basenamePrefixIndexes,
        byte[][] extensionSuffixPatterns,
        int[] extensionSuffixIndexes,
        byte[][] requiredExtensionPatterns,
        int[] requiredExtensionIndexes,
        AhoCorasickAutomaton? prefixMatcher,
        int[] prefixIndexes,
        AhoCorasickAutomaton? suffixMatcher,
        int[] suffixIndexes,
        int[] alwaysIndexes)
    {
        this.globs = globs;
        this.literalPatterns = literalPatterns;
        this.literalIndexes = literalIndexes;
        this.basenameLiteralPatterns = basenameLiteralPatterns;
        this.basenameLiteralIndexes = basenameLiteralIndexes;
        this.basenamePrefixPatterns = basenamePrefixPatterns;
        this.basenamePrefixIndexes = basenamePrefixIndexes;
        this.extensionSuffixPatterns = extensionSuffixPatterns;
        this.extensionSuffixIndexes = extensionSuffixIndexes;
        this.requiredExtensionPatterns = requiredExtensionPatterns;
        this.requiredExtensionIndexes = requiredExtensionIndexes;
        this.prefixMatcher = prefixMatcher;
        this.prefixIndexes = prefixIndexes;
        this.suffixMatcher = suffixMatcher;
        this.suffixIndexes = suffixIndexes;
        this.alwaysIndexes = alwaysIndexes;
    }

    /// <summary>
    /// Gets the number of globs in the set.
    /// </summary>
    public int Count => globs.Length;

    /// <summary>
    /// Gets a value indicating whether this set contains no globs.
    /// </summary>
    public bool IsEmpty => globs.Length == 0;

    /// <summary>
    /// Creates a builder for an ordered glob set.
    /// </summary>
    /// <returns>A glob-set builder.</returns>
    public static GlobSetBuilder Builder()
    {
        return new GlobSetBuilder();
    }

    /// <summary>
    /// Creates a glob set from ordered glob patterns.
    /// </summary>
    /// <param name="globs">The globs in insertion order.</param>
    /// <returns>A glob set.</returns>
    public static GlobSet Create(IReadOnlyList<Glob> globs)
    {
        ArgumentNullException.ThrowIfNull(globs);

        var copy = new Glob[globs.Count];
        for (int index = 0; index < globs.Count; index++)
        {
            copy[index] = globs[index] ?? throw new ArgumentNullException(nameof(globs));
        }

        var literalPatterns = new List<byte[]>();
        var literalIndexes = new List<int>();
        var basenameLiteralPatterns = new List<byte[]>();
        var basenameLiteralIndexes = new List<int>();
        var basenamePrefixPatterns = new List<byte[]>();
        var basenamePrefixIndexes = new List<int>();
        var extensionSuffixPatterns = new List<byte[]>();
        var extensionSuffixIndexes = new List<int>();
        var requiredExtensionPatterns = new List<byte[]>();
        var requiredExtensionIndexes = new List<int>();
        var prefixPatterns = new List<byte[]>();
        var prefixIndexes = new List<int>();
        var suffixPatterns = new List<byte[]>();
        var suffixIndexes = new List<int>();
        var alwaysIndexes = new List<int>();

        for (int index = 0; index < copy.Length; index++)
        {
            AddCandidateStrategy(
                copy[index],
                index,
                literalPatterns,
                literalIndexes,
                basenameLiteralPatterns,
                basenameLiteralIndexes,
                basenamePrefixPatterns,
                basenamePrefixIndexes,
                extensionSuffixPatterns,
                extensionSuffixIndexes,
                requiredExtensionPatterns,
                requiredExtensionIndexes,
                prefixPatterns,
                prefixIndexes,
                suffixPatterns,
                suffixIndexes,
                alwaysIndexes);
        }

        return new GlobSet(
            copy,
            literalPatterns.ToArray(),
            literalIndexes.ToArray(),
            basenameLiteralPatterns.ToArray(),
            basenameLiteralIndexes.ToArray(),
            basenamePrefixPatterns.ToArray(),
            basenamePrefixIndexes.ToArray(),
            extensionSuffixPatterns.ToArray(),
            extensionSuffixIndexes.ToArray(),
            requiredExtensionPatterns.ToArray(),
            requiredExtensionIndexes.ToArray(),
            CreateAhoCorasick(copy, prefixPatterns, prefixIndexes),
            prefixIndexes.ToArray(),
            CreateAhoCorasick(copy, suffixPatterns, suffixIndexes),
            suffixIndexes.ToArray(),
            alwaysIndexes.ToArray());
    }

    /// <summary>
    /// Tests whether any glob in the set matches a path.
    /// </summary>
    /// <param name="path">The path bytes.</param>
    /// <returns><see langword="true" /> when any glob matches.</returns>
    public bool IsMatch(ReadOnlySpan<byte> path)
    {
        if (IsEmpty)
        {
            return false;
        }

        if (globs.Length <= StackCandidateLimit)
        {
            Span<bool> candidates = stackalloc bool[globs.Length];
            CollectCandidates(path, candidates);
            return AnyCandidateMatches(path, candidates);
        }

        bool[] candidateArray = new bool[globs.Length];
        CollectCandidates(path, candidateArray);
        return AnyCandidateMatches(path, candidateArray);
    }

    /// <summary>
    /// Tests whether any glob in the set matches a prepared candidate path.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <returns><see langword="true" /> when any glob matches.</returns>
    public bool IsMatch(GlobCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (IsEmpty)
        {
            return false;
        }

        if (globs.Length <= StackCandidateLimit)
        {
            Span<bool> candidates = stackalloc bool[globs.Length];
            CollectCandidates(candidate, candidates);
            return AnyCandidateMatches(candidate, candidates);
        }

        bool[] candidateArray = new bool[globs.Length];
        CollectCandidates(candidate, candidateArray);
        return AnyCandidateMatches(candidate, candidateArray);
    }

    /// <summary>
    /// Tests whether every glob in the set matches a path.
    /// </summary>
    /// <param name="path">The path bytes.</param>
    /// <returns><see langword="true" /> when all globs match, including for an empty set.</returns>
    public bool MatchesAll(ReadOnlySpan<byte> path)
    {
        for (int index = 0; index < globs.Length; index++)
        {
            if (!globs[index].IsMatch(path))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether every glob in the set matches a prepared candidate path.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <returns><see langword="true" /> when all globs match, including for an empty set.</returns>
    public bool MatchesAll(GlobCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        for (int index = 0; index < globs.Length; index++)
        {
            if (!globs[index].IsMatch(candidate))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the indexes of matching globs in insertion order.
    /// </summary>
    /// <param name="path">The path bytes.</param>
    /// <returns>The indexes of matching globs.</returns>
    public IReadOnlyList<int> MatchingIndexes(ReadOnlySpan<byte> path)
    {
        var indexes = new List<int>();
        MatchingIndexesInto(path, indexes);
        return indexes;
    }

    /// <summary>
    /// Returns the indexes of matching globs in insertion order for a prepared candidate path.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <returns>The indexes of matching globs.</returns>
    public IReadOnlyList<int> MatchingIndexes(GlobCandidate candidate)
    {
        var indexes = new List<int>();
        MatchingIndexesInto(candidate, indexes);
        return indexes;
    }

    /// <summary>
    /// Adds matching glob indexes to the supplied list in insertion order.
    /// </summary>
    /// <param name="path">The path bytes.</param>
    /// <param name="indexes">The destination collection, cleared before matching begins.</param>
    public void MatchingIndexesInto(ReadOnlySpan<byte> path, ICollection<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);

        indexes.Clear();
        if (IsEmpty)
        {
            return;
        }

        if (globs.Length <= StackCandidateLimit)
        {
            Span<bool> candidates = stackalloc bool[globs.Length];
            CollectCandidates(path, candidates);
            MatchingCandidateIndexesInto(path, candidates, indexes);
            return;
        }

        bool[] candidateArray = new bool[globs.Length];
        CollectCandidates(path, candidateArray);
        MatchingCandidateIndexesInto(path, candidateArray, indexes);
    }

    /// <summary>
    /// Adds matching glob indexes for a prepared candidate path to the supplied collection in insertion order.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <param name="indexes">The destination collection, cleared before matching begins.</param>
    public void MatchingIndexesInto(GlobCandidate candidate, ICollection<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(indexes);

        indexes.Clear();
        if (IsEmpty)
        {
            return;
        }

        if (globs.Length <= StackCandidateLimit)
        {
            Span<bool> candidates = stackalloc bool[globs.Length];
            CollectCandidates(candidate, candidates);
            MatchingCandidateIndexesInto(candidate, candidates, indexes);
            return;
        }

        bool[] candidateArray = new bool[globs.Length];
        CollectCandidates(candidate, candidateArray);
        MatchingCandidateIndexesInto(candidate, candidateArray, indexes);
    }

    /// <summary>
    /// Returns the last matching glob index, preserving insertion-order precedence.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <param name="eligible">Optional per-glob eligibility flags; an empty span treats all globs as eligible.</param>
    /// <returns>The last matching glob index, or <c>-1</c> when no eligible glob matches.</returns>
    public int LastMatchingIndex(GlobCandidate candidate, ReadOnlySpan<bool> eligible)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!eligible.IsEmpty && eligible.Length != globs.Length)
        {
            throw new ArgumentException("Eligibility flags must be empty or match the glob count.", nameof(eligible));
        }

        if (IsEmpty)
        {
            return -1;
        }

        if (globs.Length <= StackCandidateLimit)
        {
            Span<bool> candidates = stackalloc bool[globs.Length];
            CollectCandidates(candidate, candidates);
            return LastMatchingCandidateIndex(candidate, candidates, eligible);
        }

        bool[] candidateArray = new bool[globs.Length];
        CollectCandidates(candidate, candidateArray);
        return LastMatchingCandidateIndex(candidate, candidateArray, eligible);
    }

    private static void AddCandidateStrategy(
        Glob glob,
        int globIndex,
        List<byte[]> literalPatterns,
        List<int> literalIndexes,
        List<byte[]> basenameLiteralPatterns,
        List<int> basenameLiteralIndexes,
        List<byte[]> basenamePrefixPatterns,
        List<int> basenamePrefixIndexes,
        List<byte[]> extensionSuffixPatterns,
        List<int> extensionSuffixIndexes,
        List<byte[]> requiredExtensionPatterns,
        List<int> requiredExtensionIndexes,
        List<byte[]> prefixPatterns,
        List<int> prefixIndexes,
        List<byte[]> suffixPatterns,
        List<int> suffixIndexes,
        List<int> alwaysIndexes)
    {
        if (glob.TryGetLiteral(out byte[] literal))
        {
            if (glob.FixedPrefixAppliesToBaseName())
            {
                basenameLiteralPatterns.Add(literal);
                basenameLiteralIndexes.Add(globIndex);
                return;
            }

            literalPatterns.Add(literal);
            literalIndexes.Add(globIndex);
            return;
        }

        bool classified = false;
        if (glob.TryGetFixedPrefix(out byte[] prefix))
        {
            if (glob.FixedPrefixAppliesToBaseName())
            {
                basenamePrefixPatterns.Add(prefix);
                basenamePrefixIndexes.Add(globIndex);
            }
            else
            {
                prefixPatterns.Add(prefix);
                prefixIndexes.Add(globIndex);
            }

            classified = true;
        }

        bool hasComponentSuffix = glob.TryGetComponentSuffix(out byte[] componentSuffix, out byte[] componentExactLiteral);
        if (hasComponentSuffix)
        {
            suffixPatterns.Add(componentSuffix);
            suffixIndexes.Add(globIndex);
            literalPatterns.Add(componentExactLiteral);
            literalIndexes.Add(globIndex);
            classified = true;
        }

        bool hasExtensionOnly = glob.TryGetExtensionOnly(out _);
        if (!hasExtensionOnly && glob.TryGetRequiredExtension(out byte[] requiredExtension))
        {
            requiredExtensionPatterns.Add(requiredExtension);
            requiredExtensionIndexes.Add(globIndex);
            classified = true;
        }

        if (!hasComponentSuffix && glob.TryGetFixedSuffix(out byte[] suffix))
        {
            if (glob.IsExtensionSuffix(suffix))
            {
                extensionSuffixPatterns.Add(suffix);
                extensionSuffixIndexes.Add(globIndex);
            }
            else
            {
                suffixPatterns.Add(suffix);
                suffixIndexes.Add(globIndex);
            }

            classified = true;
        }

        if (!classified)
        {
            alwaysIndexes.Add(globIndex);
        }
    }

    private static AhoCorasickAutomaton? CreateAhoCorasick(
        Glob[] globs,
        List<byte[]> patterns,
        List<int> patternIndexes)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        bool asciiCaseInsensitive = false;
        for (int index = 0; index < patternIndexes.Count; index++)
        {
            if (globs[patternIndexes[index]].IsAsciiCaseInsensitive())
            {
                asciiCaseInsensitive = true;
                break;
            }
        }

        return AhoCorasickAutomaton.Create(patterns, AhoCorasickMatchKind.Standard, asciiCaseInsensitive);
    }

    private void CollectCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        CollectLiteralCandidates(path, candidates);
        CollectBaseNameLiteralCandidates(path, candidates);
        CollectBaseNamePrefixCandidates(path, candidates);
        CollectExtensionSuffixCandidates(path, candidates);
        CollectRequiredExtensionCandidates(path, candidates);
        CollectPrefixCandidates(path, candidates);
        CollectSuffixCandidates(path, candidates);
        CollectAlwaysCandidates(candidates);
    }

    private void CollectCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        CollectLiteralCandidates(candidate, candidates);
        CollectBaseNameLiteralCandidates(candidate, candidates);
        CollectBaseNamePrefixCandidates(candidate, candidates);
        CollectExtensionSuffixCandidates(candidate, candidates);
        CollectRequiredExtensionCandidates(candidate, candidates);
        CollectPrefixCandidates(candidate.Path.Span, candidates);
        CollectSuffixCandidates(candidate.Path.Span, candidates);
        CollectAlwaysCandidates(candidates);
    }

    private void CollectLiteralCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        for (int index = 0; index < literalPatterns.Length; index++)
        {
            int globIndex = literalIndexes[index];
            if (globs[globIndex].LiteralEquals(path, literalPatterns[index]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectLiteralCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        ReadOnlySpan<byte> path = candidate.Path.Span;
        CollectLiteralCandidates(path, candidates);
    }

    private void CollectBaseNameLiteralCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        for (int index = 0; index < basenameLiteralPatterns.Length; index++)
        {
            int globIndex = basenameLiteralIndexes[index];
            if (globs[globIndex].BaseNameLiteralEquals(path, basenameLiteralPatterns[index]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectBaseNameLiteralCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        ReadOnlySpan<byte> baseName = candidate.BaseName.Span;
        for (int index = 0; index < basenameLiteralPatterns.Length; index++)
        {
            int globIndex = basenameLiteralIndexes[index];
            if (globs[globIndex].LiteralEquals(baseName, basenameLiteralPatterns[index]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectBaseNamePrefixCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        for (int index = 0; index < basenamePrefixPatterns.Length; index++)
        {
            int globIndex = basenamePrefixIndexes[index];
            if (globs[globIndex].BaseNameStartsWith(path, basenamePrefixPatterns[index]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectBaseNamePrefixCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        ReadOnlySpan<byte> baseName = candidate.BaseName.Span;
        for (int index = 0; index < basenamePrefixPatterns.Length; index++)
        {
            int globIndex = basenamePrefixIndexes[index];
            ReadOnlySpan<byte> prefix = basenamePrefixPatterns[index];
            if (baseName.Length >= prefix.Length && globs[globIndex].LiteralEquals(baseName[..prefix.Length], prefix))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectExtensionSuffixCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        for (int index = 0; index < extensionSuffixPatterns.Length; index++)
        {
            int globIndex = extensionSuffixIndexes[index];
            if (globs[globIndex].PathEndsWith(path, extensionSuffixPatterns[index]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectExtensionSuffixCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        CollectExtensionSuffixCandidates(candidate.Path.Span, candidates);
    }

    private void CollectRequiredExtensionCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        for (int patternId = 0; patternId < requiredExtensionPatterns.Length; patternId++)
        {
            int globIndex = requiredExtensionIndexes[patternId];
            if (globs[globIndex].PathExtensionEquals(path, requiredExtensionPatterns[patternId]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectRequiredExtensionCandidates(GlobCandidate candidate, Span<bool> candidates)
    {
        for (int patternId = 0; patternId < requiredExtensionPatterns.Length; patternId++)
        {
            int globIndex = requiredExtensionIndexes[patternId];
            if (globs[globIndex].BaseNameExtensionEquals(candidate.BaseName.Span, requiredExtensionPatterns[patternId]))
            {
                candidates[globIndex] = true;
            }
        }
    }

    private void CollectAlwaysCandidates(Span<bool> candidates)
    {
        for (int index = 0; index < alwaysIndexes.Length; index++)
        {
            candidates[alwaysIndexes[index]] = true;
        }
    }

    private void CollectPrefixCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        if (prefixMatcher is null)
        {
            return;
        }

        AhoCorasickOverlappingEnumerator matches = prefixMatcher.EnumerateOverlapping(path);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (match.Start == 0)
            {
                candidates[prefixIndexes[match.PatternId]] = true;
            }
        }
    }

    private void CollectSuffixCandidates(ReadOnlySpan<byte> path, Span<bool> candidates)
    {
        if (suffixMatcher is null)
        {
            return;
        }

        AhoCorasickOverlappingEnumerator matches = suffixMatcher.EnumerateOverlapping(path);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (match.End == path.Length)
            {
                candidates[suffixIndexes[match.PatternId]] = true;
            }
        }
    }

    private bool AnyCandidateMatches(ReadOnlySpan<byte> path, ReadOnlySpan<bool> candidates)
    {
        for (int index = 0; index < globs.Length; index++)
        {
            if (candidates[index] && globs[index].IsMatch(path))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyCandidateMatches(GlobCandidate candidate, ReadOnlySpan<bool> candidates)
    {
        for (int index = 0; index < globs.Length; index++)
        {
            if (candidates[index] && globs[index].IsMatch(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private void MatchingCandidateIndexesInto(ReadOnlySpan<byte> path, ReadOnlySpan<bool> candidates, ICollection<int> indexes)
    {
        for (int index = 0; index < globs.Length; index++)
        {
            if (candidates[index] && globs[index].IsMatch(path))
            {
                indexes.Add(index);
            }
        }
    }

    private void MatchingCandidateIndexesInto(GlobCandidate candidate, ReadOnlySpan<bool> candidates, ICollection<int> indexes)
    {
        for (int index = 0; index < globs.Length; index++)
        {
            if (candidates[index] && globs[index].IsMatch(candidate))
            {
                indexes.Add(index);
            }
        }
    }

    private int LastMatchingCandidateIndex(GlobCandidate candidate, ReadOnlySpan<bool> candidates, ReadOnlySpan<bool> eligible)
    {
        int matchedIndex = -1;
        for (int index = 0; index < globs.Length; index++)
        {
            if (candidates[index] &&
                (eligible.IsEmpty || eligible[index]) &&
                globs[index].IsMatch(candidate))
            {
                matchedIndex = index;
            }
        }

        return matchedIndex;
    }
}
