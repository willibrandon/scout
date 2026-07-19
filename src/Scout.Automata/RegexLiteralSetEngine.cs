using System.Buffers;
using System.Text;

namespace Scout;

/// <summary>
/// Executes an exact set of literal alternatives selected by the regex compiler.
/// </summary>
internal sealed class RegexLiteralSetEngine
{
    private const int MinimumLiteralCount = 1;
    private const int PrefixBytes = 3;
    private const int MinimumUnicodePrefixBytes = 4;
    private const int MaxUnicodePrefixVariants = 128;
    private const int LargeLiteralSetThreshold = 32;
    private const int SmallLiteralFinderSetThreshold = 8;
    private const int TwoByteSearchPatternBucketThreshold = 8;
    private const int IndependentAsciiCaseInsensitiveCountThreshold = 4;
    private const int SingleLiteralIndexOfAnchorScoreThreshold = 240;
    private const int ThreeByteSingleLiteralIndexOfAnchorScoreThreshold = 100;
    private const int SingleLiteralFirstByteAnchorScoreThreshold = 250;
    private const int SingleLiteralMemmemProbeLength = 512;

    private readonly AhoCorasickAutomaton? automaton;
    private readonly byte[][] literals;
    private readonly byte[][] searchPatterns;
    private readonly int[] searchPatternLiteralIds;
    private readonly int[]?[]? commonFoldedLiterals;
    private readonly bool asciiCaseInsensitive;
    private readonly bool unicodeCaseInsensitive;
    private readonly int maxLiteralLength;
    private readonly RegexAnchoredLiteralFinder? singleAnchoredLiteralFinder;
    private readonly bool singleLiteralIndexOf;
    private readonly bool singleLiteralFirstByte;
    private readonly MemmemFinder? singleLiteralFinder;
    private readonly RegexAsciiCaseInsensitiveFinder? singleAsciiCaseInsensitiveFinder;
    private readonly RegexAsciiCaseInsensitiveLiteralSetScanner? asciiCaseInsensitiveScanner;
    private readonly RegexAsciiCaseInsensitiveFinder[]? independentAsciiCaseInsensitiveCountFinders;
    private readonly RegexCommonPrefixLiteralSetScanner? _commonPrefixScanner;
    private readonly bool sharedFirstByteScanner;
    private readonly byte sharedFirstByte;
    private readonly RegexPackedLiteralSetScanner? packedLiteralScanner;
    private readonly RegexShortLiteralSetScanner? shortLiteralScanner;
    private readonly RegexCaseSensitiveLiteralSetScanner? caseSensitiveScanner;
    private readonly RegexLargeLiteralTrieScanner? largeLiteralTrieScanner;
    private readonly RegexLargeLiteralSetScanner? largeLiteralScanner;
    private readonly MemmemFinder[]? smallLiteralFinders;
    private readonly RegexLiteralPrefixScanner? prefixScanner;
    private readonly int[][] searchPatternIndexesByFirstByte;
    private readonly Dictionary<ushort, int[]>? searchPatternIndexesByFirstTwoBytes;

    private RegexLiteralSetEngine(
        IReadOnlyList<byte[]> literals,
        IReadOnlyList<byte[]> searchPatterns,
        IReadOnlyList<int> searchPatternLiteralIds,
        bool asciiCaseInsensitive,
        bool unicodeCaseInsensitive,
        bool useAho,
        RegexLargeLiteralTrieScanner? prebuiltLargeLiteralTrieScanner = null,
        RegexLargeLiteralSetScanner? prebuiltLargeLiteralScanner = null,
        bool takeLiteralOwnership = false)
    {
        this.literals = new byte[literals.Count][];
        for (int index = 0; index < literals.Count; index++)
        {
            byte[] literal = takeLiteralOwnership
                ? literals[index]
                : literals[index].ToArray();
            this.literals[index] = literal;
            maxLiteralLength = Math.Max(maxLiteralLength, literal.Length);
        }

        this.searchPatterns = [];
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.unicodeCaseInsensitive = unicodeCaseInsensitive;
        this.searchPatternLiteralIds = [];
        searchPatternIndexesByFirstByte = [];
        searchPatternIndexesByFirstTwoBytes = null;

        if (unicodeCaseInsensitive)
        {
            commonFoldedLiterals = BuildCommonFoldedLiterals(this.literals);
        }

        if (this.literals.Length == 1 && searchPatterns.Count == 0)
        {
            if (!RegexAnchoredLiteralFinder.TryCreate(this.literals[0], out singleAnchoredLiteralFinder))
            {
                singleLiteralFinder = new MemmemFinder(this.literals[0]);
                if (ShouldUseSingleLiteralFirstByte(this.literals[0]))
                {
                    singleLiteralFirstByte = true;
                }

                if (ShouldUseSingleLiteralIndexOf(this.literals[0]))
                {
                    singleLiteralIndexOf = true;
                }
            }

            return;
        }

        if (this.literals.Length == 1 &&
            asciiCaseInsensitive &&
            !unicodeCaseInsensitive)
        {
            singleAsciiCaseInsensitiveFinder = new RegexAsciiCaseInsensitiveFinder(this.literals[0]);
            return;
        }

        if (this.literals.Length > 1 &&
            asciiCaseInsensitive &&
            !unicodeCaseInsensitive)
        {
            asciiCaseInsensitiveScanner = new RegexAsciiCaseInsensitiveLiteralSetScanner(this.literals);
            if (CanCountAsciiCaseInsensitiveLiteralsIndependently(this.literals))
            {
                independentAsciiCaseInsensitiveCountFinders = CreateAsciiCaseInsensitiveFinders(this.literals);
            }

            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            RegexCommonPrefixLiteralSetScanner.TryCreate(
                this.literals,
                out _commonPrefixScanner,
                takeLiteralOwnership: true))
        {
            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            !useAho &&
            TryGetSharedSelectiveFirstByte(this.literals, out sharedFirstByte))
        {
            sharedFirstByteScanner = true;
            return;
        }

        if (this.literals.Length > 1 &&
            unicodeCaseInsensitive &&
            !useAho &&
            RegexPackedLiteralSetScanner.TryCreateCommonCyrillicCaseInsensitive(
                this.literals,
                out RegexPackedLiteralSetScanner? commonCyrillic))
        {
            packedLiteralScanner = commonCyrillic;
            return;
        }

        if (ShouldUseSmallLiteralFinders(this.literals, asciiCaseInsensitive, unicodeCaseInsensitive, useAho) &&
            this.literals.Length <= 5 &&
            !ContainsUtf8ScalarsLongerThanTwoBytes(this.literals))
        {
            if (!asciiCaseInsensitive &&
                !unicodeCaseInsensitive &&
                !useAho &&
                !ContainsNonAscii(this.literals) &&
                RegexShortLiteralSetScanner.TryCreate(this.literals, out RegexShortLiteralSetScanner? shortScanner))
            {
                shortLiteralScanner = shortScanner;
                return;
            }

            smallLiteralFinders = CreateSmallLiteralFinders(this.literals);
            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            !useAho &&
            ContainsUtf8ScalarsLongerThanTwoBytes(this.literals) &&
            RegexShortLiteralSetScanner.TryCreate(this.literals, out RegexShortLiteralSetScanner? nonAsciiShortScanner))
        {
            shortLiteralScanner = nonAsciiShortScanner;
            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            !useAho &&
            ContainsNonAscii(this.literals) &&
            RegexPackedLiteralSetScanner.TryCreate(this.literals, out RegexPackedLiteralSetScanner? packed))
        {
            packedLiteralScanner = packed;
            return;
        }

        if (ShouldUseSmallLiteralFinders(this.literals, asciiCaseInsensitive, unicodeCaseInsensitive, useAho))
        {
            if (RegexShortLiteralSetScanner.TryCreate(this.literals, out RegexShortLiteralSetScanner? shortScanner))
            {
                shortLiteralScanner = shortScanner;
                return;
            }
        }

        if (ShouldUseEarlySmallLiteralFinders(this.literals, asciiCaseInsensitive, unicodeCaseInsensitive, useAho))
        {
            smallLiteralFinders = CreateSmallLiteralFinders(this.literals);
            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            !useAho &&
            RegexPackedLiteralSetScanner.TryCreate(this.literals, out packed))
        {
            packedLiteralScanner = packed;
            return;
        }

        if (this.literals.Length > 1 &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            !useAho &&
            RegexCaseSensitiveLiteralSetScanner.TryCreate(
                this.literals,
                out RegexCaseSensitiveLiteralSetScanner? caseSensitive,
                useChunkedCount: !ContainsNonAscii(this.literals)))
        {
            caseSensitiveScanner = caseSensitive;
            return;
        }

        if (ShouldUseSmallLiteralFinders(this.literals, asciiCaseInsensitive, unicodeCaseInsensitive, useAho))
        {
            smallLiteralFinders = CreateSmallLiteralFinders(this.literals);
            return;
        }

        if (prebuiltLargeLiteralTrieScanner is not null)
        {
            largeLiteralTrieScanner = prebuiltLargeLiteralTrieScanner;
            return;
        }

        if (prebuiltLargeLiteralScanner is not null)
        {
            largeLiteralScanner = prebuiltLargeLiteralScanner;
            return;
        }

        if (useAho &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            RegexLargeLiteralTrieScanner.TryCreate(this.literals, out RegexLargeLiteralTrieScanner? trieScanner))
        {
            largeLiteralTrieScanner = trieScanner;
            return;
        }

        if (useAho &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            RegexLargeLiteralSetScanner.TryCreate(this.literals, out RegexLargeLiteralSetScanner? scanner))
        {
            largeLiteralScanner = scanner;
            return;
        }

        if (useAho)
        {
            this.searchPatterns = CopyByteArrays(searchPatterns);
            this.searchPatternLiteralIds = CopyIntArray(searchPatternLiteralIds);
            automaton = AhoCorasickAutomaton
                .Builder()
                .WithMatchKind(AhoCorasickMatchKind.Standard)
                .WithStartKind(AhoCorasickStartKind.Unanchored)
                .WithAsciiCaseInsensitive(asciiCaseInsensitive)
                .Build(this.searchPatterns);
            return;
        }

        this.searchPatterns = CopyByteArrays(searchPatterns);
        this.searchPatternLiteralIds = CopyIntArray(searchPatternLiteralIds);
        searchPatternIndexesByFirstByte = BuildSearchPatternBuckets(this.searchPatterns);
        searchPatternIndexesByFirstTwoBytes = BuildTwoByteSearchPatternBuckets(this.searchPatterns);
        prefixScanner = new RegexLiteralPrefixScanner(this.searchPatterns);
    }

    /// <summary>
    /// Creates an exact literal-set engine from compiler-proven syntax alternatives.
    /// </summary>
    /// <param name="root">The parsed syntax root.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="engine">Receives the literal-set engine.</param>
    /// <param name="allowCharacterClasses">Whether finite character classes may be expanded into literals.</param>
    /// <returns><see langword="true" /> when every alternative is a non-empty literal with compatible case semantics.</returns>
    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexLiteralSetEngine? engine,
        bool allowCharacterClasses = true)
    {
        engine = null;
        if (!RegexFiniteLiteralExtractor.TryExtract(
                root,
                options,
                out List<byte[]> literals,
                out bool? asciiCaseInsensitive,
                out bool? literalUnicodeClasses,
                out bool containsCharacterClass) ||
            (containsCharacterClass && !allowCharacterClasses) ||
            literals.Count < MinimumLiteralCount ||
            literals.Exists(static literal => literal.Length == 0))
        {
            return false;
        }

        return TryCreateFromLiterals(literals, asciiCaseInsensitive, literalUnicodeClasses, out engine);
    }

    /// <summary>
    /// Gets the sole case-sensitive literal represented by this exact engine.
    /// </summary>
    /// <param name="literal">Receives the immutable literal bytes.</param>
    /// <returns><see langword="true" /> when the engine contains exactly one case-sensitive literal.</returns>
    internal bool TryGetSingleCaseSensitiveLiteral(out ReadOnlyMemory<byte> literal)
    {
        if (literals.Length == 1 && !asciiCaseInsensitive && !unicodeCaseInsensitive)
        {
            literal = literals[0];
            return true;
        }

        literal = default;
        return false;
    }

    /// <summary>
    /// Gets a value indicating whether searches use the compiler-proven common-prefix scanner.
    /// </summary>
    internal bool UsesCommonPrefixScanner => _commonPrefixScanner is not null;

    /// <summary>
    /// Attempts to count non-overlapping matches while the selected exact-literal scan detects NUL bytes.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="count">Receives the non-overlapping match count.</param>
    /// <param name="containsNul">Receives whether the complete haystack contains a NUL byte.</param>
    /// <returns><see langword="true" /> when one complete exact-literal scan produced both results.</returns>
    internal bool TryCountMatchesAndDetectNul(
        ReadOnlySpan<byte> haystack,
        out long count,
        out bool containsNul)
    {
        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.TryCountMatchesAndDetectNul(
                haystack,
                out count,
                out containsNul);
        }

        count = 0;
        containsNul = false;
        return false;
    }

    internal static bool TryCreateFromLiterals(
        List<byte[]> literals,
        bool? asciiCaseInsensitive,
        bool? literalUnicodeClasses,
        out RegexLiteralSetEngine? engine,
        bool takeLiteralOwnership = false)
    {
        engine = null;
        bool unicodeCaseInsensitive = asciiCaseInsensitive == true && literalUnicodeClasses == true;
        bool asciiOnlyCaseInsensitive = asciiCaseInsensitive == true && !unicodeCaseInsensitive;
        if (!asciiOnlyCaseInsensitive &&
            !unicodeCaseInsensitive &&
            RegexCommonPrefixLiteralSetScanner.CanCreate(literals))
        {
            engine = new RegexLiteralSetEngine(
                literals,
                searchPatterns: [],
                searchPatternLiteralIds: [],
                asciiCaseInsensitive: false,
                unicodeCaseInsensitive: false,
                useAho: false,
                takeLiteralOwnership: takeLiteralOwnership);
            return true;
        }

        bool useAho = ShouldUseAho(literals.Count, asciiOnlyCaseInsensitive, unicodeCaseInsensitive);
        byte[][] searchPatterns;
        int[] searchPatternLiteralIds;
        RegexLargeLiteralTrieScanner? largeLiteralTrieScanner = null;
        RegexLargeLiteralSetScanner? largeLiteralScanner = null;
        if (useAho &&
            !asciiOnlyCaseInsensitive &&
            !unicodeCaseInsensitive &&
            (RegexLargeLiteralSetScanner.TryCreate(literals, out largeLiteralScanner) ||
             RegexLargeLiteralTrieScanner.TryCreate(literals, out largeLiteralTrieScanner)))
        {
            searchPatterns = [];
            searchPatternLiteralIds = [];
        }
        else if (asciiOnlyCaseInsensitive && literals.Count > 1)
        {
            searchPatterns = [];
            searchPatternLiteralIds = [];
        }
        else if (!TryBuildSearchPatterns(
                     literals,
                     asciiOnlyCaseInsensitive,
                     unicodeCaseInsensitive,
                     useAho,
                     out searchPatterns,
                     out searchPatternLiteralIds))
        {
            return false;
        }

        engine = new RegexLiteralSetEngine(
            literals,
            searchPatterns,
            searchPatternLiteralIds,
            asciiOnlyCaseInsensitive,
            unicodeCaseInsensitive,
            useAho,
            largeLiteralTrieScanner,
            largeLiteralScanner,
            takeLiteralOwnership);
        return true;
    }

    /// <summary>
    /// Finds the leftmost-first literal match at or after an offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The selected match, or <see langword="null" /> when no literal matches.</returns>
    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (singleAnchoredLiteralFinder is not null)
        {
            int start = singleAnchoredLiteralFinder.Find(haystack, startOffset);
            return start < 0
                ? null
                : new RegexMatch(start, literals[0].Length);
        }

        if (singleLiteralIndexOf)
        {
            if (ShouldUseSingleLiteralMemmem(haystack, startOffset))
            {
                int memmemOffset = singleLiteralFinder!.Find(haystack[startOffset..]);
                return memmemOffset < 0
                    ? null
                    : new RegexMatch(startOffset + memmemOffset, literals[0].Length);
            }

            if (singleLiteralFirstByte)
            {
                int firstByteOffset = FindSingleLiteralByFirstByte(haystack, startOffset);
                return firstByteOffset < 0
                    ? null
                    : new RegexMatch(firstByteOffset, literals[0].Length);
            }

            int offset = haystack[startOffset..].IndexOf(literals[0]);
            return offset < 0
                ? null
                : new RegexMatch(startOffset + offset, literals[0].Length);
        }

        if (singleLiteralFinder is not null)
        {
            int offset = singleLiteralFinder.Find(haystack[startOffset..]);
            return offset < 0
                ? null
                : new RegexMatch(startOffset + offset, literals[0].Length);
        }

        if (singleAsciiCaseInsensitiveFinder is not null)
        {
            int offset = singleAsciiCaseInsensitiveFinder.Find(haystack[startOffset..]);
            return offset < 0
                ? null
                : new RegexMatch(startOffset + offset, literals[0].Length);
        }

        if (asciiCaseInsensitiveScanner is not null)
        {
            return asciiCaseInsensitiveScanner.Find(haystack, startOffset)?.Match;
        }

        if (_commonPrefixScanner is not null)
        {
            return _commonPrefixScanner.Find(haystack, startOffset)?.Match;
        }

        if (sharedFirstByteScanner)
        {
            return FindSharedFirstByteLiteralSet(haystack, startOffset);
        }

        if (caseSensitiveScanner is not null)
        {
            return caseSensitiveScanner.Find(haystack, startOffset)?.Match;
        }

        if (packedLiteralScanner is not null)
        {
            return packedLiteralScanner.Find(haystack, startOffset)?.Match;
        }

        if (shortLiteralScanner is not null)
        {
            return shortLiteralScanner.Find(haystack, startOffset)?.Match;
        }

        if (smallLiteralFinders is not null)
        {
            return FindSmallLiteralSet(haystack, startOffset);
        }

        if (largeLiteralTrieScanner is not null)
        {
            return largeLiteralTrieScanner.Find(haystack, startOffset)?.Match;
        }

        if (largeLiteralScanner is not null)
        {
            return largeLiteralScanner.Find(haystack, startOffset)?.Match;
        }

        if (automaton is not null)
        {
            return FindAho(haystack, startOffset);
        }

        for (int candidateStart = prefixScanner!.FindCandidate(haystack, startOffset);
             candidateStart >= 0;
             candidateStart = prefixScanner.FindCandidate(haystack, candidateStart + 1))
        {
            if (TryFindLiteralAtSearchPatternCandidate(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
            {
                return candidate.Match;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a source-ordered literal match beginning exactly at an offset.
    /// </summary>
    /// <param name="haystack">The bytes to inspect.</param>
    /// <param name="startAt">The required match start.</param>
    /// <returns>The selected match, or <see langword="null" /> when no literal begins at the offset.</returns>
    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        return TryFindLiteralAt(haystack, startOffset, out RegexLiteralSetCandidate candidate)
            ? candidate.Match
            : null;
    }

    /// <summary>
    /// Finds the shortest literal at the earliest matching position.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The earliest shortest match, or <see langword="null" /> when no literal matches.</returns>
    public RegexMatch? FindEarliest(ReadOnlySpan<byte> haystack, int startAt)
    {
        RegexMatch? first = Find(haystack, startAt);
        return first.HasValue
            ? FindShortestAt(haystack, first.Value.Start)
            : null;
    }

    /// <summary>
    /// Finds the longest literal beginning exactly at an offset.
    /// </summary>
    /// <param name="haystack">The bytes to inspect.</param>
    /// <param name="startAt">The required match start.</param>
    /// <returns>The longest match, or <see langword="null" /> when no literal begins at the offset.</returns>
    public RegexMatch? FindAllKindAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        RegexMatch? best = null;
        for (int index = 0; index < literals.Length; index++)
        {
            if (!TryMatchLiteralAt(haystack, startOffset, index, out int length) ||
                best.HasValue && length <= best.Value.Length)
            {
                continue;
            }

            best = new RegexMatch(startOffset, length);
        }

        return best;
    }

    /// <summary>
    /// Finds every literal beginning exactly at an offset in ascending match-length order.
    /// </summary>
    /// <param name="haystack">The bytes to inspect.</param>
    /// <param name="startAt">The required match start.</param>
    /// <returns>The overlapping matches.</returns>
    public IReadOnlyList<RegexMatch> FindOverlappingAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        var matches = new List<RegexMatch>();
        for (int index = 0; index < literals.Length; index++)
        {
            if (TryMatchLiteralAt(haystack, startOffset, index, out int length))
            {
                matches.Add(new RegexMatch(startOffset, length));
            }
        }

        matches.Sort(static (left, right) => left.Length.CompareTo(right.Length));
        return matches;
    }

    /// <summary>
    /// Counts source-ordered, non-overlapping matches at or after an offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The match count.</returns>
    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumNonOverlapping(haystack, startAt, sumSpans: false);
    }

    /// <summary>
    /// Sums the lengths of source-ordered, non-overlapping matches at or after an offset.
    /// </summary>
    /// <param name="haystack">The bytes to search.</param>
    /// <param name="startAt">The first permitted match start.</param>
    /// <returns>The sum of match lengths.</returns>
    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSumNonOverlapping(haystack, startAt, sumSpans: true);
    }

    private long CountOrSumNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        if (singleAnchoredLiteralFinder is not null)
        {
            return singleAnchoredLiteralFinder.CountOrSum(haystack, startOffset, sumSpans);
        }

        if (singleLiteralIndexOf)
        {
            if (ShouldUseSingleLiteralMemmem(haystack, startOffset))
            {
                return CountOrSumSingleLiteral(haystack, startOffset, sumSpans);
            }

            if (singleLiteralFirstByte)
            {
                return CountOrSumSingleLiteralFirstByte(haystack, startOffset, sumSpans);
            }

            return CountOrSumSingleLiteralIndexOf(haystack, startOffset, sumSpans);
        }

        if (singleLiteralFinder is not null)
        {
            return CountOrSumSingleLiteral(haystack, startOffset, sumSpans);
        }

        if (singleAsciiCaseInsensitiveFinder is not null)
        {
            return CountOrSumSingleAsciiCaseInsensitiveLiteral(haystack, startOffset, sumSpans);
        }

        if (asciiCaseInsensitiveScanner is not null)
        {
            return CountOrSumAsciiCaseInsensitiveLiteralSet(haystack, startOffset, sumSpans);
        }

        if (_commonPrefixScanner is not null)
        {
            return sumSpans
                ? _commonPrefixScanner.SumMatchSpans(haystack, startOffset)
                : _commonPrefixScanner.CountMatches(haystack, startOffset);
        }

        if (sharedFirstByteScanner)
        {
            return CountOrSumSharedFirstByteLiteralSet(haystack, startOffset, sumSpans);
        }

        if (caseSensitiveScanner is not null)
        {
            return caseSensitiveScanner.CountOrSum(haystack, startOffset, sumSpans);
        }

        if (packedLiteralScanner is not null)
        {
            return packedLiteralScanner.CountOrSum(haystack, startOffset, sumSpans);
        }

        if (shortLiteralScanner is not null)
        {
            return shortLiteralScanner.CountOrSum(haystack, startOffset, sumSpans);
        }

        if (smallLiteralFinders is not null)
        {
            return CountOrSumSmallLiteralSet(haystack, startOffset, sumSpans);
        }

        if (largeLiteralTrieScanner is not null)
        {
            return largeLiteralTrieScanner.CountOrSum(haystack, startOffset, sumSpans);
        }

        if (largeLiteralScanner is not null)
        {
            return sumSpans
                ? largeLiteralScanner.SumMatchSpans(haystack, startOffset)
                : largeLiteralScanner.CountMatches(haystack, startOffset);
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

            if (TryFindLiteralAtSearchPatternCandidate(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
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

    private RegexMatch? FindSmallLiteralSet(ReadOnlySpan<byte> haystack, int startOffset)
    {
        RegexLiteralSetCandidate? best = null;
        for (int index = 0; index < smallLiteralFinders!.Length; index++)
        {
            int offset = smallLiteralFinders[index].Find(haystack[startOffset..]);
            if (offset < 0)
            {
                continue;
            }

            var candidate = new RegexLiteralSetCandidate(
                index,
                new RegexMatch(startOffset + offset, literals[index].Length));
            if (IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best.HasValue ? best.Value.Match : null;
    }

    private long CountOrSumSmallLiteralSet(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        MemmemFinder[] finders = smallLiteralFinders!;
        Span<int> nextSearchStarts = stackalloc int[finders.Length];
        int exhausted = haystack.Length + 1;
        long total = 0;
        int nextAllowedStart = startOffset;
        nextSearchStarts.Fill(-1);
        while (true)
        {
            int bestLiteral = -1;
            int bestStart = int.MaxValue;
            for (int index = 0; index < finders.Length; index++)
            {
                int start = nextSearchStarts[index];
                if (start == exhausted)
                {
                    continue;
                }

                if (start < nextAllowedStart)
                {
                    int offset = finders[index].Find(haystack[nextAllowedStart..]);
                    if (offset < 0)
                    {
                        nextSearchStarts[index] = exhausted;
                        continue;
                    }

                    start = nextAllowedStart + offset;
                    nextSearchStarts[index] = start;
                }

                if (start < bestStart ||
                    start == bestStart && index < bestLiteral)
                {
                    bestLiteral = index;
                    bestStart = start;
                }
            }

            if (bestLiteral < 0)
            {
                return total;
            }

            int length = literals[bestLiteral].Length;
            total += sumSpans ? length : 1;
            int end = bestStart + length;
            nextAllowedStart = end;
            nextSearchStarts[bestLiteral] = -1;
        }
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

            RegexLiteralSetCandidate candidate = ResolveAhoCandidate(startOffset, match);
            if (!IsBetter(candidate, best))
            {
                continue;
            }

            best = candidate;
        }

        return best.HasValue ? best.Value.Match : null;
    }

    private long CountOrSumAho(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int nextAllowedStart = startOffset;
        var pending = new List<RegexLiteralSetCandidate>();
        AhoCorasickOverlappingEnumerator matches = automaton!.EnumerateOverlapping(haystack[startOffset..]);
        while (matches.MoveNext())
        {
            AhoCorasickMatch ahoMatch = matches.Current;
            RegexLiteralSetCandidate candidate = ResolveAhoCandidate(startOffset, ahoMatch);
            if (candidate.Match.Start >= nextAllowedStart)
            {
                pending.Add(candidate);
            }

            DrainResolvedAhoCandidates(
                pending,
                startOffset + ahoMatch.End,
                ref nextAllowedStart,
                sumSpans,
                ref total);
        }

        while (pending.Count != 0)
        {
            AcceptBestAhoCandidate(pending, ref nextAllowedStart, sumSpans, ref total);
        }

        return total;
    }

    private void DrainResolvedAhoCandidates(
        List<RegexLiteralSetCandidate> pending,
        int observedEnd,
        ref int nextAllowedStart,
        bool sumSpans,
        ref long total)
    {
        while (pending.Count != 0)
        {
            int earliestStart = int.MaxValue;
            for (int index = 0; index < pending.Count; index++)
            {
                earliestStart = Math.Min(earliestStart, pending[index].Match.Start);
            }

            if (observedEnd <= earliestStart + maxLiteralLength)
            {
                return;
            }

            AcceptBestAhoCandidate(pending, ref nextAllowedStart, sumSpans, ref total);
        }
    }

    private static void AcceptBestAhoCandidate(
        List<RegexLiteralSetCandidate> pending,
        ref int nextAllowedStart,
        bool sumSpans,
        ref long total)
    {
        int bestIndex = -1;
        RegexLiteralSetCandidate best = default;
        for (int index = 0; index < pending.Count; index++)
        {
            RegexLiteralSetCandidate candidate = pending[index];
            if (candidate.Match.Start < nextAllowedStart)
            {
                continue;
            }

            if (bestIndex < 0 || IsBetter(candidate, best))
            {
                bestIndex = index;
                best = candidate;
            }
        }

        if (bestIndex < 0)
        {
            pending.Clear();
            return;
        }

        RegexMatch match = best.Match;
        total += sumSpans ? match.Length : 1;
        nextAllowedStart = match.End;
        RemovePendingBefore(pending, nextAllowedStart);
    }

    private static void RemovePendingBefore(List<RegexLiteralSetCandidate> pending, int start)
    {
        int write = 0;
        for (int read = 0; read < pending.Count; read++)
        {
            RegexLiteralSetCandidate candidate = pending[read];
            if (candidate.Match.Start >= start)
            {
                pending[write] = candidate;
                write++;
            }
        }

        if (write < pending.Count)
        {
            pending.RemoveRange(write, pending.Count - write);
        }
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

    private long CountOrSumSingleLiteralIndexOf(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int position = startOffset;
        byte[] literal = literals[0];
        int length = literal.Length;
        while (position <= haystack.Length)
        {
            int offset = haystack[position..].IndexOf(literal);
            if (offset < 0)
            {
                return total;
            }

            total += sumSpans ? length : 1;
            position += offset + length;
        }

        return total;
    }

    private long CountOrSumSingleLiteralFirstByte(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int position = startOffset;
        byte[] literal = literals[0];
        int length = literal.Length;
        int lastStart = haystack.Length - length;
        byte first = literal[0];
        while (position <= lastStart)
        {
            int offset = haystack[position..].IndexOf(first);
            if (offset < 0)
            {
                return total;
            }

            int start = position + offset;
            if (start > lastStart)
            {
                return total;
            }

            if (haystack.Slice(start, length).SequenceEqual(literal))
            {
                total += sumSpans ? length : 1;
                position = start + length;
            }
            else
            {
                position = start + 1;
            }
        }

        return total;
    }

    private int FindSingleLiteralByFirstByte(ReadOnlySpan<byte> haystack, int startOffset)
    {
        byte[] literal = literals[0];
        int length = literal.Length;
        int position = startOffset;
        int lastStart = haystack.Length - length;
        byte first = literal[0];
        while (position <= lastStart)
        {
            int offset = haystack[position..].IndexOf(first);
            if (offset < 0)
            {
                return -1;
            }

            int start = position + offset;
            if (start > lastStart)
            {
                return -1;
            }

            if (haystack.Slice(start, length).SequenceEqual(literal))
            {
                return start;
            }

            position = start + 1;
        }

        return -1;
    }

    private bool ShouldUseSingleLiteralMemmem(ReadOnlySpan<byte> haystack, int startOffset)
    {
        byte[] literal = literals[0];
        if (singleLiteralFinder is null ||
            literal.Length < 3 ||
            startOffset > haystack.Length - literal.Length)
        {
            return false;
        }

        int probeLength = Math.Min(SingleLiteralMemmemProbeLength, haystack.Length - startOffset);
        if (probeLength < literal.Length)
        {
            return false;
        }

        return singleLiteralFinder.Find(haystack.Slice(startOffset, probeLength)) >= 0;
    }

    private long CountOrSumSingleAsciiCaseInsensitiveLiteral(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        return singleAsciiCaseInsensitiveFinder!.CountOrSum(haystack, startOffset, sumSpans);
    }

    private long CountOrSumAsciiCaseInsensitiveLiteralSet(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        if (independentAsciiCaseInsensitiveCountFinders is not null)
        {
            return CountOrSumIndependentAsciiCaseInsensitiveLiteralSet(haystack, startOffset, sumSpans);
        }

        return asciiCaseInsensitiveScanner!.CountOrSum(haystack, startOffset, sumSpans);
    }

    private long CountOrSumIndependentAsciiCaseInsensitiveLiteralSet(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        RegexAsciiCaseInsensitiveFinder[] finders = independentAsciiCaseInsensitiveCountFinders!;
        long total = 0;
        for (int index = 0; index < finders.Length; index++)
        {
            total += finders[index].CountOrSum(haystack, startOffset, sumSpans);
        }

        return total;
    }

    private RegexMatch? FindSharedFirstByteLiteralSet(ReadOnlySpan<byte> haystack, int startOffset)
    {
        int searchAt = startOffset;
        while (searchAt < haystack.Length)
        {
            int relative = MemchrSearch.Find(haystack[searchAt..], sharedFirstByte);
            if (relative < 0)
            {
                return null;
            }

            int candidateStart = searchAt + relative;
            if (TryFindLiteralAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
            {
                return candidate.Match;
            }

            searchAt = candidateStart + 1;
        }

        return null;
    }

    private long CountOrSumSharedFirstByteLiteralSet(ReadOnlySpan<byte> haystack, int startOffset, bool sumSpans)
    {
        long total = 0;
        int searchAt = startOffset;
        while (searchAt < haystack.Length)
        {
            int relative = MemchrSearch.Find(haystack[searchAt..], sharedFirstByte);
            if (relative < 0)
            {
                return total;
            }

            int candidateStart = searchAt + relative;
            if (TryFindLiteralAt(haystack, candidateStart, out RegexLiteralSetCandidate candidate))
            {
                RegexMatch match = candidate.Match;
                total += sumSpans ? match.Length : 1;
                searchAt = match.End;
                continue;
            }

            searchAt = candidateStart + 1;
        }

        return total;
    }

    private RegexLiteralSetCandidate ResolveAhoCandidate(int startOffset, AhoCorasickMatch match)
    {
        int literalId = searchPatternLiteralIds[match.PatternId];
        int start = startOffset + match.Start;
        return new RegexLiteralSetCandidate(literalId, new RegexMatch(start, match.Length));
    }

    private bool TryFindLiteralAt(ReadOnlySpan<byte> haystack, int start, out RegexLiteralSetCandidate candidate)
    {
        for (int index = 0; index < literals.Length; index++)
        {
            if (TryMatchLiteralAt(haystack, start, index, out int length))
            {
                candidate = new RegexLiteralSetCandidate(index, new RegexMatch(start, length));
                return true;
            }
        }

        candidate = default;
        return false;
    }

    private bool TryFindLiteralAtSearchPatternCandidate(
        ReadOnlySpan<byte> haystack,
        int start,
        out RegexLiteralSetCandidate candidate)
    {
        RegexLiteralSetCandidate? best = null;
        if ((uint)start >= (uint)haystack.Length)
        {
            candidate = default;
            return false;
        }

        if (searchPatternIndexesByFirstTwoBytes is not null ||
            searchPatterns.Length > TwoByteSearchPatternBucketThreshold)
        {
            return TryFindLiteralAtBucketedSearchPatternCandidate(haystack, start, out candidate);
        }

        for (int index = 0; index < searchPatterns.Length; index++)
        {
            ReadOnlySpan<byte> searchPattern = searchPatterns[index];
            if (searchPattern.Length > haystack.Length - start ||
                haystack[start + searchPattern.Length - 1] != searchPattern[^1] ||
                !haystack.Slice(start, searchPattern.Length).SequenceEqual(searchPattern))
            {
                continue;
            }

            int literalId = searchPatternLiteralIds[index];
            if (!TryMatchLiteralAt(haystack, start, literalId, out int length))
            {
                continue;
            }

            var current = new RegexLiteralSetCandidate(literalId, new RegexMatch(start, length));
            if (IsBetter(current, best))
            {
                best = current;
            }
        }

        candidate = best.GetValueOrDefault();
        return best.HasValue;
    }

    private bool TryFindLiteralAtBucketedSearchPatternCandidate(
        ReadOnlySpan<byte> haystack,
        int start,
        out RegexLiteralSetCandidate candidate)
    {
        RegexLiteralSetCandidate? best = null;
        int[] candidatePatternIndexes;
        if (searchPatternIndexesByFirstTwoBytes is not null)
        {
            if (start + 1 >= haystack.Length ||
                !searchPatternIndexesByFirstTwoBytes.TryGetValue(BlockKey(haystack[start..]), out int[]? twoBytePatternIndexes))
            {
                candidate = default;
                return false;
            }

            candidatePatternIndexes = twoBytePatternIndexes;
        }
        else
        {
            candidatePatternIndexes = searchPatternIndexesByFirstByte[haystack[start]];
        }

        for (int bucketIndex = 0; bucketIndex < candidatePatternIndexes.Length; bucketIndex++)
        {
            int index = candidatePatternIndexes[bucketIndex];
            ReadOnlySpan<byte> searchPattern = searchPatterns[index];
            if (searchPattern.Length > haystack.Length - start ||
                haystack[start + searchPattern.Length - 1] != searchPattern[^1] ||
                !haystack.Slice(start, searchPattern.Length).SequenceEqual(searchPattern))
            {
                continue;
            }

            int literalId = searchPatternLiteralIds[index];
            if (!TryMatchLiteralAt(haystack, start, literalId, out int length))
            {
                continue;
            }

            var current = new RegexLiteralSetCandidate(literalId, new RegexMatch(start, length));
            if (IsBetter(current, best))
            {
                best = current;
            }
        }

        candidate = best.GetValueOrDefault();
        return best.HasValue;
    }

    private static int[][] BuildSearchPatternBuckets(byte[][] searchPatterns)
    {
        var buckets = new List<int>[256];
        for (int index = 0; index < searchPatterns.Length; index++)
        {
            byte[] pattern = searchPatterns[index];
            if (pattern.Length == 0)
            {
                continue;
            }

            (buckets[pattern[0]] ??= []).Add(index);
        }

        int[][] indexesByFirstByte = new int[256][];
        for (int index = 0; index < buckets.Length; index++)
        {
            indexesByFirstByte[index] = buckets[index]?.ToArray() ?? [];
        }

        return indexesByFirstByte;
    }

    private static Dictionary<ushort, int[]>? BuildTwoByteSearchPatternBuckets(byte[][] searchPatterns)
    {
        if (searchPatterns.Length <= TwoByteSearchPatternBucketThreshold)
        {
            return null;
        }

        var buckets = new Dictionary<ushort, List<int>>();
        for (int index = 0; index < searchPatterns.Length; index++)
        {
            byte[] pattern = searchPatterns[index];
            if (pattern.Length < 2)
            {
                return null;
            }

            ushort key = BlockKey(pattern);
            if (!buckets.TryGetValue(key, out List<int>? indexes))
            {
                indexes = [];
                buckets.Add(key, indexes);
            }

            indexes.Add(index);
        }

        var compact = new Dictionary<ushort, int[]>(buckets.Count);
        foreach (KeyValuePair<ushort, List<int>> bucket in buckets)
        {
            compact.Add(bucket.Key, bucket.Value.ToArray());
        }

        return compact;
    }

    private static ushort BlockKey(ReadOnlySpan<byte> value)
    {
        return (ushort)(value[0] | (value[1] << 8));
    }

    private RegexMatch? FindShortestAt(ReadOnlySpan<byte> haystack, int start)
    {
        RegexMatch? best = null;
        for (int index = 0; index < literals.Length; index++)
        {
            if (!TryMatchLiteralAt(haystack, start, index, out int length) ||
                best.HasValue && length >= best.Value.Length)
            {
                continue;
            }

            best = new RegexMatch(start, length);
        }

        return best;
    }

    private static byte[][] CopyByteArrays(IReadOnlyList<byte[]> values)
    {
        byte[][] copy = new byte[values.Count][];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index].ToArray();
        }

        return copy;
    }

    private static int[] CopyIntArray(IReadOnlyList<int> values)
    {
        int[] copy = new int[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index];
        }

        return copy;
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

    private static bool ShouldUseSingleLiteralIndexOf(ReadOnlySpan<byte> literal)
    {
        if (literal.Length == 3)
        {
            return ContainsAsciiAnchorAtLeast(literal, ThreeByteSingleLiteralIndexOfAnchorScoreThreshold);
        }

        if (literal.Length < 4)
        {
            return false;
        }

        return ContainsAsciiAnchorAtLeast(literal, SingleLiteralIndexOfAnchorScoreThreshold);
    }

    private static bool ShouldUseSingleLiteralFirstByte(ReadOnlySpan<byte> literal)
    {
        return literal.Length >= 4 &&
            literal[0] <= 0x7F &&
            RegexAnchoredLiteralFinder.AsciiAnchorScore(literal[0]) >= SingleLiteralFirstByteAnchorScoreThreshold;
    }

    private static bool ContainsAsciiAnchorAtLeast(ReadOnlySpan<byte> literal, int threshold)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            byte value = literal[index];
            if (value <= 0x7F &&
                RegexAnchoredLiteralFinder.AsciiAnchorScore(value) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldUseSmallLiteralFinders(
        byte[][] literals,
        bool asciiCaseInsensitive,
        bool unicodeCaseInsensitive,
        bool useAho)
    {
        return !useAho &&
            !asciiCaseInsensitive &&
            !unicodeCaseInsensitive &&
            literals.Length is > 1 and <= SmallLiteralFinderSetThreshold &&
            ShouldUseMemmemLiteralFinders(literals);
    }

    private static bool ShouldUseEarlySmallLiteralFinders(
        byte[][] literals,
        bool asciiCaseInsensitive,
        bool unicodeCaseInsensitive,
        bool useAho)
    {
        return ShouldUseSmallLiteralFinders(literals, asciiCaseInsensitive, unicodeCaseInsensitive, useAho) &&
            (literals.Length == 2 &&
                Math.Min(literals[0].Length, literals[1].Length) >= 4 ||
            literals.Length == 3 &&
                Math.Min(Math.Min(literals[0].Length, literals[1].Length), literals[2].Length) >= 4);
    }

    private static MemmemFinder[] CreateSmallLiteralFinders(byte[][] literals)
    {
        var finders = new MemmemFinder[literals.Length];
        for (int index = 0; index < literals.Length; index++)
        {
            finders[index] = new MemmemFinder(literals[index]);
        }

        return finders;
    }

    private static bool ShouldUseMemmemLiteralFinders(byte[][] literals)
    {
        int minLength = int.MaxValue;
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            minLength = Math.Min(minLength, literal.Length);
            for (int byteIndex = 0; byteIndex < literal.Length; byteIndex++)
            {
                if (literal[byteIndex] > 0x7F)
                {
                    return true;
                }
            }
        }

        return minLength >= 3;
    }

    private static bool ContainsNonAscii(byte[][] literals)
    {
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int byteIndex = 0; byteIndex < literal.Length; byteIndex++)
            {
                if (literal[byteIndex] > 0x7F)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsUtf8ScalarsLongerThanTwoBytes(byte[][] literals)
    {
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            byte[] literal = literals[literalIndex];
            for (int byteIndex = 0; byteIndex < literal.Length; byteIndex++)
            {
                if (literal[byteIndex] >= 0xE0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetSharedSelectiveFirstByte(byte[][] literals, out byte firstByte)
    {
        firstByte = 0;
        if (literals.Length != 2 || literals[0].Length == 0)
        {
            return false;
        }

        firstByte = literals[0][0];
        if (firstByte < 0xE0 &&
            ExactCaseAnchorScore(firstByte) < 200)
        {
            firstByte = 0;
            return false;
        }

        for (int index = 1; index < literals.Length; index++)
        {
            if (literals[index].Length == 0 || literals[index][0] != firstByte)
            {
                firstByte = 0;
                return false;
            }
        }

        return true;
    }

    private static int ExactCaseAnchorScore(byte value)
    {
        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            return 220;
        }

        return RegexAnchoredLiteralFinder.AsciiAnchorScore(value);
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
        searchPatterns.Add(pattern);
        searchPatternLiteralIds.Add(literalId);
    }

    private bool TryMatchLiteralAt(ReadOnlySpan<byte> haystack, int start, int literalId, out int length)
    {
        if ((uint)start > (uint)haystack.Length)
        {
            length = 0;
            return false;
        }

        byte[] literal = literals[literalId];
        if (unicodeCaseInsensitive)
        {
            if (commonFoldedLiterals?[literalId] is int[] commonFoldedLiteral)
            {
                if (TryMatchCommonFoldedLiteralAt(haystack[start..], commonFoldedLiteral, out length, out bool known))
                {
                    return true;
                }

                if (known)
                {
                    length = 0;
                    return false;
                }
            }

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
        if (TryMatchCommonUtf8CaseInsensitiveLiteralAt(haystack, literal, out length, out bool known))
        {
            return true;
        }

        if (known)
        {
            length = 0;
            return false;
        }

        ReadOnlySpan<byte> remainingHaystack = haystack;
        ReadOnlySpan<byte> remainingLiteral = literal;
        while (!remainingLiteral.IsEmpty)
        {
            if (!TryReadUtf8Scalar(remainingLiteral, out int literalScalar, out int literalConsumed) ||
                !TryReadUtf8Scalar(remainingHaystack, out int haystackScalar, out int haystackConsumed) ||
                !SimpleCaseFoldEquals(haystackScalar, literalScalar))
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

    private static int[]?[] BuildCommonFoldedLiterals(byte[][] literals)
    {
        int[]?[] folded = new int[]?[literals.Length];
        for (int literalIndex = 0; literalIndex < literals.Length; literalIndex++)
        {
            folded[literalIndex] = TryBuildCommonFoldedLiteral(literals[literalIndex]);
        }

        return folded;
    }

    private static int[]? TryBuildCommonFoldedLiteral(byte[] literal)
    {
        List<int> folded = [];
        int index = 0;
        while (index < literal.Length)
        {
            if (!TryReadCommonFoldedScalar(literal, index, out int scalar, out int consumed))
            {
                return null;
            }

            folded.Add(scalar);
            index += consumed;
        }

        return folded.ToArray();
    }

    private static bool TryMatchCommonFoldedLiteralAt(
        ReadOnlySpan<byte> haystack,
        int[] literal,
        out int length,
        out bool known)
    {
        length = 0;
        known = true;
        int haystackIndex = 0;
        for (int literalIndex = 0; literalIndex < literal.Length; literalIndex++)
        {
            if (!TryReadCommonFoldedScalar(haystack, haystackIndex, out int haystackFolded, out int haystackConsumed))
            {
                known = false;
                length = 0;
                return false;
            }

            if (literal[literalIndex] != haystackFolded)
            {
                length = 0;
                return false;
            }

            haystackIndex += haystackConsumed;
            length += haystackConsumed;
        }

        return true;
    }

    private static bool TryMatchCommonUtf8CaseInsensitiveLiteralAt(
        ReadOnlySpan<byte> haystack,
        byte[] literal,
        out int length,
        out bool known)
    {
        length = 0;
        known = true;
        int haystackIndex = 0;
        int literalIndex = 0;
        while (literalIndex < literal.Length)
        {
            if (!TryReadCommonFoldedScalar(literal, literalIndex, out int literalFolded, out int literalConsumed) ||
                !TryReadCommonFoldedScalar(haystack, haystackIndex, out int haystackFolded, out int haystackConsumed))
            {
                known = false;
                length = 0;
                return false;
            }

            if (literalFolded != haystackFolded)
            {
                length = 0;
                return false;
            }

            literalIndex += literalConsumed;
            haystackIndex += haystackConsumed;
            length += haystackConsumed;
        }

        return true;
    }

    private static bool TryReadCommonFoldedScalar(
        ReadOnlySpan<byte> bytes,
        int index,
        out int folded,
        out int consumed)
    {
        folded = 0;
        consumed = 0;
        if ((uint)index >= (uint)bytes.Length)
        {
            return false;
        }

        byte first = bytes[index];
        if (first <= 0x7F)
        {
            folded = FastSimpleFold(first);
            consumed = 1;
            return true;
        }

        if (index + 1 >= bytes.Length)
        {
            return false;
        }

        byte second = bytes[index + 1];
        if (first == 0xD0)
        {
            if (second is >= 0x90 and <= 0x9F)
            {
                folded = 0x0430 + (second - 0x90);
                consumed = 2;
                return true;
            }

            if (second is >= 0xA0 and <= 0xAF)
            {
                folded = 0x0440 + (second - 0xA0);
                consumed = 2;
                return true;
            }

            if (second is >= 0xB0 and <= 0xBF)
            {
                folded = 0x0430 + (second - 0xB0);
                consumed = 2;
                return true;
            }

            if (second == 0x81)
            {
                folded = 0x0451;
                consumed = 2;
                return true;
            }
        }
        else if (first == 0xD1)
        {
            if (second is >= 0x80 and <= 0x8F)
            {
                folded = 0x0440 + (second - 0x80);
                consumed = 2;
                return true;
            }

            if (second == 0x91)
            {
                folded = 0x0451;
                consumed = 2;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadUtf8Scalar(ReadOnlySpan<byte> bytes, out int scalar, out int consumed)
    {
        scalar = 0;
        consumed = 0;
        if (bytes.IsEmpty)
        {
            return false;
        }

        byte first = bytes[0];
        if (first <= 0x7F)
        {
            scalar = first;
            consumed = 1;
            return true;
        }

        if (first is >= 0xC2 and <= 0xDF &&
            bytes.Length >= 2 &&
            bytes[1] is >= 0x80 and <= 0xBF)
        {
            scalar = ((first & 0x1F) << 6) | (bytes[1] & 0x3F);
            consumed = 2;
            return true;
        }

        OperationStatus status = Rune.DecodeFromUtf8(bytes, out Rune rune, out consumed);
        if (status != OperationStatus.Done || consumed <= 0)
        {
            scalar = 0;
            consumed = 0;
            return false;
        }

        scalar = rune.Value;
        return true;
    }

    private static bool SimpleCaseFoldEquals(int left, int right)
    {
        if (left == right || FastSimpleFold(left) == FastSimpleFold(right))
        {
            return true;
        }

        return Rune.IsValid(left) &&
            Rune.IsValid(right) &&
            RegexUnicodeTables.IsSimpleCaseFold(new Rune(left), new Rune(right));
    }

    private static int FastSimpleFold(int scalar)
    {
        if ((uint)(scalar - 'A') <= 'Z' - 'A')
        {
            return scalar + 32;
        }

        if ((uint)(scalar - 0x0410) <= 0x042F - 0x0410)
        {
            return scalar + 0x20;
        }

        return scalar == 0x0401 ? 0x0451 : scalar;
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

    private static bool CanCountAsciiCaseInsensitiveLiteralsIndependently(byte[][] literals)
    {
        if (literals.Length is <= 1 or > IndependentAsciiCaseInsensitiveCountThreshold)
        {
            return false;
        }

        byte[][] normalized = new byte[literals.Length][];
        for (int index = 0; index < literals.Length; index++)
        {
            if (literals[index].Length == 0)
            {
                return false;
            }

            normalized[index] = NormalizeAsciiCase(literals[index]);
        }

        for (int left = 0; left < normalized.Length; left++)
        {
            for (int right = left + 1; right < normalized.Length; right++)
            {
                if (CanOverlap(normalized[left], normalized[right]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CanOverlap(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int rightStart = 1 - right.Length; rightStart < left.Length; rightStart++)
        {
            int leftOffset = Math.Max(0, rightStart);
            int rightOffset = Math.Max(0, -rightStart);
            int overlap = Math.Min(left.Length - leftOffset, right.Length - rightOffset);
            if (overlap > 0 &&
                left.Slice(leftOffset, overlap).SequenceEqual(right.Slice(rightOffset, overlap)))
            {
                return true;
            }
        }

        return false;
    }

    private static RegexAsciiCaseInsensitiveFinder[] CreateAsciiCaseInsensitiveFinders(byte[][] literals)
    {
        var finders = new RegexAsciiCaseInsensitiveFinder[literals.Length];
        for (int index = 0; index < literals.Length; index++)
        {
            finders[index] = new RegexAsciiCaseInsensitiveFinder(literals[index]);
        }

        return finders;
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
