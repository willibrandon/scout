using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexLiteralSetEngine
{
    private const int MinimumLiteralCount = 1;
    private const int PrefixBytes = 3;
    private const int MinimumUnicodePrefixBytes = 4;
    private const int MaxUnicodePrefixVariants = 128;
    private const int LargeLiteralSetThreshold = 32;

    private readonly AhoCorasickAutomaton? automaton;
    private readonly byte[][] literals;
    private readonly int[] searchPatternLiteralIds;
    private readonly bool asciiCaseInsensitive;
    private readonly bool unicodeCaseInsensitive;
    private readonly int maxLiteralLength;
    private readonly MemmemFinder? singleLiteralFinder;
    private readonly RegexLiteralPrefixScanner? prefixScanner;

    private RegexLiteralSetEngine(
        IReadOnlyList<byte[]> literals,
        IReadOnlyList<byte[]> searchPatterns,
        IReadOnlyList<int> searchPatternLiteralIds,
        bool asciiCaseInsensitive,
        bool unicodeCaseInsensitive,
        bool useAho)
    {
        this.literals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            this.literals[index] = literals[index].ToArray();
            maxLiteralLength = Math.Max(maxLiteralLength, this.literals[index].Length);
        }

        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.unicodeCaseInsensitive = unicodeCaseInsensitive;
        this.searchPatternLiteralIds = new int[searchPatternLiteralIds.Count];
        for (int index = 0; index < searchPatternLiteralIds.Count; index++)
        {
            this.searchPatternLiteralIds[index] = searchPatternLiteralIds[index];
        }

        if (searchPatterns.Count == 0)
        {
            singleLiteralFinder = new MemmemFinder(this.literals[0]);
            return;
        }

        if (useAho)
        {
            automaton = AhoCorasickAutomaton
                .Builder()
                .WithMatchKind(AhoCorasickMatchKind.Standard)
                .WithStartKind(AhoCorasickStartKind.Unanchored)
                .WithAsciiCaseInsensitive(asciiCaseInsensitive)
                .Build(searchPatterns);
            return;
        }

        prefixScanner = new RegexLiteralPrefixScanner(searchPatterns);
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLiteralSetEngine? engine)
    {
        engine = null;
        var literals = new List<byte[]>();
        bool? asciiCaseInsensitive = null;
        bool? literalUnicodeClasses = null;
        if (!TryCollectLiteralBranches(root, options, literals, ref asciiCaseInsensitive, ref literalUnicodeClasses) ||
            literals.Count < MinimumLiteralCount)
        {
            return false;
        }

        bool unicodeCaseInsensitive = asciiCaseInsensitive == true && literalUnicodeClasses == true;
        bool asciiOnlyCaseInsensitive = asciiCaseInsensitive == true && !unicodeCaseInsensitive;
        bool useAho = ShouldUseAho(literals.Count, asciiOnlyCaseInsensitive, unicodeCaseInsensitive);
        if (!TryBuildSearchPatterns(
            literals,
            asciiOnlyCaseInsensitive,
            unicodeCaseInsensitive,
            useAho,
            out byte[][] searchPatterns,
            out int[] searchPatternLiteralIds))
        {
            return false;
        }

        engine = new RegexLiteralSetEngine(
            literals,
            searchPatterns,
            searchPatternLiteralIds,
            asciiOnlyCaseInsensitive,
            unicodeCaseInsensitive,
            useAho);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (singleLiteralFinder is not null)
        {
            int offset = singleLiteralFinder.Find(haystack[startOffset..]);
            return offset < 0
                ? null
                : new RegexMatch(startOffset + offset, literals[0].Length);
        }

        if (automaton is not null)
        {
            return FindAho(haystack, startOffset);
        }

        for (int candidateStart = prefixScanner!.FindCandidate(haystack, startOffset);
             candidateStart >= 0;
             candidateStart = prefixScanner.FindCandidate(haystack, candidateStart + 1))
        {
            if (TryFindLiteralAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
            {
                return candidate.Match;
            }
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        return TryFindLiteralAt(haystack, startOffset, out RegexLiteralSetCandidate candidate)
            ? candidate.Match
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumNonOverlapping(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumNonOverlapping(haystack, startAt, sumSpans: true);
    }

    private long CountOrSumNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (singleLiteralFinder is not null)
        {
            return CountOrSumSingleLiteral(haystack, startOffset, sumSpans);
        }

        if (automaton is not null)
        {
            return CountOrSumAho(haystack, startOffset, sumSpans);
        }

        long total = 0;
        for (int searchAt = startOffset; searchAt <= haystack.Length;)
        {
            int candidateStart = prefixScanner!.FindCandidate(haystack, searchAt);
            if (candidateStart < 0)
            {
                return total;
            }

            if (TryFindLiteralAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
            {
                total += sumSpans ? candidate.Match.Length : 1;
                searchAt = candidate.Match.End;
            }
            else
            {
                searchAt = candidateStart + 1;
            }
        }

        return total;
    }

    private RegexMatch? FindAho(ReadOnlySpan<byte> haystack, int startOffset)
    {
        AhoCorasickOverlappingEnumerator matches = automaton!.EnumerateOverlapping(haystack[startOffset..]);
        RegexLiteralSetCandidate? best = null;
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (best.HasValue && startOffset + match.End > best.Value.Match.Start + maxLiteralLength)
            {
                break;
            }

            if (!TryResolveAhoCandidate(haystack, startOffset, match, out RegexLiteralSetCandidate candidate) ||
                !IsBetter(candidate, best))
            {
                continue;
            }

            best = candidate;
        }

        return best.HasValue ? best.Value.Match : null;
    }

    private long CountOrSumAho(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        var candidates = new List<RegexLiteralSetCandidate>();
        AhoCorasickOverlappingEnumerator matches = automaton!.EnumerateOverlapping(haystack[startOffset..]);
        while (matches.MoveNext())
        {
            AhoCorasickMatch match = matches.Current;
            if (TryResolveAhoCandidate(haystack, startOffset, match, out RegexLiteralSetCandidate candidate))
            {
                AddCandidate(candidates, candidate);
            }
        }

        candidates.Sort(static (left, right) =>
        {
            int startComparison = left.Match.Start.CompareTo(right.Match.Start);
            return startComparison != 0
                ? startComparison
                : left.LiteralId.CompareTo(right.LiteralId);
        });

        long total = 0;
        int nextAllowedStart = startOffset;
        for (int index = 0; index < candidates.Count; index++)
        {
            RegexMatch match = candidates[index].Match;
            if (match.Start < nextAllowedStart)
            {
                continue;
            }

            total += sumSpans ? match.Length : 1;
            nextAllowedStart = match.End;
        }

        return total;
    }

    private long CountOrSumSingleLiteral(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int position = startOffset;
        int length = literals[0].Length;
        while (position <= haystack.Length)
        {
            int offset = singleLiteralFinder!.Find(haystack[position..]);
            if (offset < 0)
            {
                return total;
            }

            total += sumSpans ? length : 1;
            position += offset + length;
        }

        return total;
    }

    private bool TryResolveAhoCandidate(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        AhoCorasickMatch match,
        out RegexLiteralSetCandidate candidate)
    {
        int literalId = searchPatternLiteralIds[match.PatternId];
        int start = startOffset + match.Start;
        if (TryMatchLiteralAt(haystack, start, literals[literalId], out int length))
        {
            candidate = new RegexLiteralSetCandidate(literalId, new RegexMatch(start, length));
            return true;
        }

        candidate = default;
        return false;
    }

    private static void AddCandidate(List<RegexLiteralSetCandidate> candidates, RegexLiteralSetCandidate candidate)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            RegexLiteralSetCandidate existing = candidates[index];
            if (existing.LiteralId == candidate.LiteralId &&
                existing.Match.Equals(candidate.Match))
            {
                return;
            }
        }

        candidates.Add(candidate);
    }

    private bool TryFindLiteralAt(ReadOnlySpan<byte> haystack, int start, out RegexLiteralSetCandidate candidate)
    {
        for (int index = 0; index < literals.Length; index++)
        {
            if (TryMatchLiteralAt(haystack, start, literals[index], out int length))
            {
                candidate = new RegexLiteralSetCandidate(index, new RegexMatch(start, length));
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private static bool TryCollectLiteralBranches(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte[]> literals,
        ref bool? asciiCaseInsensitive,
        ref bool? literalUnicodeClasses)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexAlternationNode alternation)
        {
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryCollectLiteralBranches(
                    alternation.Alternatives[index],
                    options,
                    literals,
                    ref asciiCaseInsensitive,
                    ref literalUnicodeClasses))
                {
                    return false;
                }
            }

            return true;
        }

        var literal = new List<byte>();
        return TryAppendLiteral(node, options, literal, ref asciiCaseInsensitive, ref literalUnicodeClasses) &&
            literal.Count > 0 &&
            AddLiteral(literals, literal.ToArray());
    }

    private static bool TryAppendLiteral(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive,
        ref bool? literalUnicodeClasses)
    {
        node = UnwrapTransparentGroups(node);
        switch (node.Kind)
        {
            case RegexSyntaxKind.Empty:
                return true;
            case RegexSyntaxKind.Literal:
                if (!SetCaseMode(options, ref asciiCaseInsensitive, ref literalUnicodeClasses))
                {
                    return false;
                }

                literal.AddRange(((RegexAtomNode)node).Value.ToArray());
                return true;
            case RegexSyntaxKind.Sequence:
                return TryAppendLiteralSequence(
                    (RegexSequenceNode)node,
                    options,
                    literal,
                    ref asciiCaseInsensitive,
                    ref literalUnicodeClasses);
            case RegexSyntaxKind.CapturingGroup:
            case RegexSyntaxKind.NonCapturingGroup:
                var group = (RegexGroupNode)node;
                return TryAppendLiteral(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    literal,
                    ref asciiCaseInsensitive,
                    ref literalUnicodeClasses);
            case RegexSyntaxKind.Repetition:
                return TryAppendLiteralRepetition(
                    (RegexRepetitionNode)node,
                    options,
                    literal,
                    ref asciiCaseInsensitive,
                    ref literalUnicodeClasses);
            default:
                return false;
        }
    }

    private static bool TryAppendLiteralSequence(
        RegexSequenceNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive,
        ref bool? literalUnicodeClasses)
    {
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < node.Nodes.Count; index++)
        {
            RegexSyntaxNode child = node.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryAppendLiteral(child, currentOptions, literal, ref asciiCaseInsensitive, ref literalUnicodeClasses))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendLiteralRepetition(
        RegexRepetitionNode node,
        RegexCompileOptions options,
        List<byte> literal,
        ref bool? asciiCaseInsensitive,
        ref bool? literalUnicodeClasses)
    {
        if (node.Maximum != node.Minimum)
        {
            return false;
        }

        for (int count = 0; count < node.Minimum; count++)
        {
            if (!TryAppendLiteral(node.Child, options, literal, ref asciiCaseInsensitive, ref literalUnicodeClasses))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SetCaseMode(
        RegexCompileOptions options,
        ref bool? asciiCaseInsensitive,
        ref bool? literalUnicodeClasses)
    {
        if (asciiCaseInsensitive.HasValue && asciiCaseInsensitive.Value != options.CaseInsensitive)
        {
            return false;
        }

        asciiCaseInsensitive = options.CaseInsensitive;
        if (options.CaseInsensitive)
        {
            if (literalUnicodeClasses.HasValue && literalUnicodeClasses.Value != options.UnicodeClasses)
            {
                return false;
            }

            literalUnicodeClasses = options.UnicodeClasses;
        }

        return true;
    }

    private static bool AddLiteral(List<byte[]> literals, byte[] literal)
    {
        if (literal.Length == 0)
        {
            return false;
        }

        literals.Add(literal);
        return true;
    }

    private static bool TryBuildSearchPatterns(
        List<byte[]> literals,
        bool asciiCaseInsensitive,
        bool unicodeCaseInsensitive,
        bool useAho,
        out byte[][] searchPatterns,
        out int[] searchPatternLiteralIds)
    {
        if (literals.Count == 1 && !asciiCaseInsensitive && !unicodeCaseInsensitive)
        {
            searchPatterns = [];
            searchPatternLiteralIds = [];
            return true;
        }

        var patterns = new List<byte[]>();
        var ids = new List<int>();
        for (int index = 0; index < literals.Count; index++)
        {
            if (useAho)
            {
                AddSearchPattern(patterns, ids, literals[index], index);
                continue;
            }

            if (unicodeCaseInsensitive)
            {
                if (!TryAddUnicodePrefixSearchPatterns(patterns, ids, literals[index], index))
                {
                    searchPatterns = [];
                    searchPatternLiteralIds = [];
                    return false;
                }

                continue;
            }

            if (asciiCaseInsensitive)
            {
                if (!TryAddAsciiCasePrefixSearchPatterns(patterns, ids, literals[index], index))
                {
                    searchPatterns = [];
                    searchPatternLiteralIds = [];
                    return false;
                }

                continue;
            }

            AddSearchPattern(patterns, ids, GetLiteralPrefix(literals[index]), index);
        }

        searchPatterns = patterns.ToArray();
        searchPatternLiteralIds = ids.ToArray();
        return searchPatterns.Length > 0;
    }

    private static bool ShouldUseAho(int literalCount, bool asciiCaseInsensitive, bool unicodeCaseInsensitive)
    {
        return literalCount >= LargeLiteralSetThreshold &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive;
    }

    private static bool IsBetter(RegexLiteralSetCandidate candidate, RegexLiteralSetCandidate? best)
    {
        if (!best.HasValue)
        {
            return true;
        }

        RegexLiteralSetCandidate current = best.Value;
        if (candidate.Match.Start != current.Match.Start)
        {
            return candidate.Match.Start < current.Match.Start;
        }

        return candidate.LiteralId < current.LiteralId;
    }

    private static bool TryAddUnicodePrefixSearchPatterns(
        List<byte[]> searchPatterns,
        List<int> searchPatternLiteralIds,
        byte[] literal,
        int literalId)
    {
        if (!TryDecodeRunes(literal, out Rune[] runes) || runes.Length == 0)
        {
            AddSearchPattern(searchPatterns, searchPatternLiteralIds, NormalizeAsciiCase(literal), literalId);
            return true;
        }

        List<byte[]> variants = [Array.Empty<byte>()];
        int shortestByteLength = 0;
        for (int runeIndex = 0; runeIndex < runes.Length; runeIndex++)
        {
            byte[][] runeEquivalents = GetCaseFoldEquivalentBytes(runes[runeIndex]);
            if (runeEquivalents.Length == 0 ||
                variants.Count > MaxUnicodePrefixVariants / runeEquivalents.Length)
            {
                return false;
            }

            List<byte[]> next = [];
            for (int prefixIndex = 0; prefixIndex < variants.Count; prefixIndex++)
            {
                byte[] prefix = variants[prefixIndex];
                for (int equivalentIndex = 0; equivalentIndex < runeEquivalents.Length; equivalentIndex++)
                {
                    byte[] equivalent = runeEquivalents[equivalentIndex];
                    byte[] variant = new byte[prefix.Length + equivalent.Length];
                    prefix.CopyTo(variant, 0);
                    equivalent.CopyTo(variant, prefix.Length);
                    AddDistinctLiteral(next, variant);
                }
            }

            variants = next;
            shortestByteLength += ShortestLiteralLength(runeEquivalents);
            if (shortestByteLength >= MinimumUnicodePrefixBytes)
            {
                break;
            }
        }

        for (int index = 0; index < variants.Count; index++)
        {
            AddSearchPattern(searchPatterns, searchPatternLiteralIds, variants[index], literalId);
        }

        return true;
    }

    private static bool TryAddAsciiCasePrefixSearchPatterns(
        List<byte[]> searchPatterns,
        List<int> searchPatternLiteralIds,
        byte[] literal,
        int literalId)
    {
        byte[] prefix = GetLiteralPrefix(literal);
        List<byte[]> variants = [Array.Empty<byte>()];
        for (int index = 0; index < prefix.Length; index++)
        {
            byte value = prefix[index];
            byte folded = FoldAscii(value);
            bool hasCaseVariant = folded is >= (byte)'a' and <= (byte)'z';
            int variantCount = hasCaseVariant ? 2 : 1;
            if (variants.Count > MaxUnicodePrefixVariants / variantCount)
            {
                return false;
            }

            List<byte[]> next = [];
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                byte[] current = variants[variantIndex];
                AddAsciiPrefixVariant(next, current, folded);
                if (hasCaseVariant)
                {
                    AddAsciiPrefixVariant(next, current, (byte)(folded - 32));
                }
            }

            variants = next;
        }

        for (int index = 0; index < variants.Count; index++)
        {
            AddSearchPattern(searchPatterns, searchPatternLiteralIds, variants[index], literalId);
        }

        return true;
    }

    private static void AddAsciiPrefixVariant(List<byte[]> variants, byte[] prefix, byte value)
    {
        byte[] variant = new byte[prefix.Length + 1];
        prefix.CopyTo(variant, 0);
        variant[^1] = value;
        AddDistinctLiteral(variants, variant);
    }

    private static byte[] GetLiteralPrefix(byte[] literal)
    {
        int length = Math.Min(literal.Length, PrefixBytes);
        byte[] prefix = new byte[length];
        Array.Copy(literal, prefix, length);
        return prefix;
    }

    private static void AddSearchPattern(
        List<byte[]> searchPatterns,
        List<int> searchPatternLiteralIds,
        byte[] pattern,
        int literalId)
    {
        for (int index = 0; index < searchPatterns.Count; index++)
        {
            if (searchPatterns[index].AsSpan().SequenceEqual(pattern))
            {
                return;
            }
        }

        searchPatterns.Add(pattern);
        searchPatternLiteralIds.Add(literalId);
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }

    private bool TryMatchLiteralAt(ReadOnlySpan<byte> haystack, int start, byte[] literal, out int length)
    {
        if ((uint)start > (uint)haystack.Length)
        {
            length = 0;
            return false;
        }

        if (unicodeCaseInsensitive)
        {
            return TryMatchUnicodeCaseInsensitiveLiteralAt(haystack[start..], literal, out length);
        }

        if (literal.Length > haystack.Length - start)
        {
            length = 0;
            return false;
        }

        length = literal.Length;
        return LiteralEquals(haystack.Slice(start, literal.Length), literal, asciiCaseInsensitive);
    }

    private static bool TryMatchUnicodeCaseInsensitiveLiteralAt(ReadOnlySpan<byte> haystack, byte[] literal, out int length)
    {
        length = 0;
        ReadOnlySpan<byte> remainingHaystack = haystack;
        ReadOnlySpan<byte> remainingLiteral = literal;
        while (!remainingLiteral.IsEmpty)
        {
            OperationStatus literalStatus = Rune.DecodeFromUtf8(remainingLiteral, out Rune literalRune, out int literalConsumed);
            OperationStatus haystackStatus = Rune.DecodeFromUtf8(remainingHaystack, out Rune haystackRune, out int haystackConsumed);
            if (literalStatus != OperationStatus.Done ||
                haystackStatus != OperationStatus.Done ||
                literalConsumed <= 0 ||
                haystackConsumed <= 0 ||
                !RegexUnicodeTables.IsSimpleCaseFold(haystackRune, literalRune))
            {
                length = 0;
                return false;
            }

            remainingLiteral = remainingLiteral[literalConsumed..];
            remainingHaystack = remainingHaystack[haystackConsumed..];
            length += haystackConsumed;
        }

        return true;
    }

    private static bool LiteralEquals(ReadOnlySpan<byte> haystack, byte[] literal, bool asciiCaseInsensitive)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            byte left = haystack[index];
            byte right = literal[index];
            if (asciiCaseInsensitive)
            {
                left = FoldAscii(left);
                right = FoldAscii(right);
            }

            if (left != right)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodeRunes(byte[] bytes, out Rune[] runes)
    {
        List<Rune> decoded = [];
        ReadOnlySpan<byte> remaining = bytes;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if (status != OperationStatus.Done || consumed <= 0)
            {
                runes = [];
                return false;
            }

            decoded.Add(rune);
            remaining = remaining[consumed..];
        }

        runes = decoded.ToArray();
        return true;
    }

    private static byte[][] GetCaseFoldEquivalentBytes(Rune value)
    {
        List<Rune> runeEquivalents = [];
        RegexUnicodeTables.AddSimpleCaseFoldEquivalents(value, runeEquivalents);
        List<byte[]> byteEquivalents = [];
        for (int index = 0; index < runeEquivalents.Count; index++)
        {
            Rune equivalent = runeEquivalents[index];
            byte[] encoded = equivalent.IsAscii
                ? [(byte)equivalent.Value]
                : Encoding.UTF8.GetBytes(equivalent.ToString());
            AddDistinctLiteral(byteEquivalents, encoded);
        }

        return byteEquivalents.ToArray();
    }

    private static int ShortestLiteralLength(byte[][] literals)
    {
        int shortest = int.MaxValue;
        for (int index = 0; index < literals.Length; index++)
        {
            shortest = Math.Min(shortest, literals[index].Length);
        }

        return shortest;
    }

    private static byte[] NormalizeAsciiCase(byte[] literal)
    {
        byte[] normalized = literal.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            normalized[index] = FoldAscii(normalized[index]);
        }

        return normalized;
    }

    private static void AddDistinctLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return;
            }
        }

        literals.Add(literal);
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }
}
