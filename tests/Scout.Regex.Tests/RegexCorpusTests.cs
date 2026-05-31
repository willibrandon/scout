using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Runs Scout's regex engine against the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusTests
{
    internal static readonly (string RelativePath, string[] Names)[] CorpusGroups =
        [
            ("misc.toml",
            [
                "ascii-literal",
                "ascii-literal-not",
                "ascii-literal-anchored",
                "ascii-literal-anchored-not",
                "anchor-start-end-line",
                "prefix-literal-match",
                "prefix-literal-match-ascii",
                "prefix-literal-no-match",
                "one-literal-edge",
                "terminates",
                "suffix-100",
                "suffix-200",
                "suffix-300",
                "suffix-400",
                "suffix-500",
                "suffix-600",
            ]),
            ("flags.toml",
            [
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "10",
                "11",
            ]),
            ("iter.toml",
            [
                "1",
                "2",
                "empty1",
                "empty2",
                "empty3",
                "empty4",
                "empty5",
                "empty6",
                "empty7",
                "empty8",
                "empty9",
                "empty10",
                "empty11",
                "start1",
                "start2",
                "anchored1",
                "anchored2",
                "anchored3",
                "nonempty-followedby-empty",
                "nonempty-followedby-oneempty",
                "nonempty-followedby-onemixed",
                "nonempty-followedby-twomixed",
            ]),
            ("empty.toml",
            [
                "100",
                "110",
                "120",
                "130",
                "200",
                "210",
                "220",
                "230",
                "240",
                "300",
                "310",
                "320",
                "330",
                "400",
                "500",
                "510",
                "520",
                "600",
                "610",
            ]),
            ("crazy.toml",
            [
                "nothing-empty",
                "nothing-something",
                "ranges",
                "ranges-not",
                "float1",
                "float2",
                "float3",
                "float4",
                "float5",
                "email",
                "email-not",
                "email-big",
                "date1",
                "date2",
                "date3",
                "start-end-empty",
                "start-end-empty-rev",
                "start-end-empty-many-1",
                "start-end-empty-many-2",
                "start-end-empty-rep",
                "start-end-empty-rep-rev",
                "neg-class-letter",
                "neg-class-letter-comma",
                "neg-class-letter-space",
                "neg-class-comma",
                "neg-class-space",
                "neg-class-space-comma",
                "neg-class-comma-space",
                "neg-class-ascii",
                "lazy-many-many",
                "lazy-many-optional",
                "lazy-one-many-many",
                "lazy-one-many-optional",
                "lazy-range-min-many",
                "lazy-range-many",
                "greedy-many-many",
                "greedy-many-optional",
                "greedy-one-many-many",
                "greedy-one-many-optional",
                "greedy-range-min-many",
                "greedy-range-many",
                "empty1",
                "empty2",
                "empty3",
                "empty4",
                "empty5",
                "empty6",
                "empty7",
                "empty8",
                "empty9",
                "empty10",
                "empty11",
            ]),
            ("multiline.toml",
            [
                "basic1",
                "basic1-crlf",
                "basic1-crlf-cr",
                "basic2",
                "basic2-crlf",
                "basic2-crlf-cr",
                "basic3",
                "basic3-crlf",
                "basic3-crlf-cr",
                "basic4",
                "basic4-crlf",
                "basic4-crlf-cr",
                "basic5",
                "basic5-crlf",
                "basic5-crlf-cr",
                "basic6",
                "basic6-crlf",
                "basic6-crlf-cr",
                "basic7",
                "basic7-crlf",
                "basic7-crlf-cr",
                "basic8",
                "basic8-crlf",
                "basic8-crlf-cr",
                "basic9",
                "basic9-crlf",
                "repeat1",
                "repeat1-crlf",
                "repeat1-crlf-cr",
                "repeat1-no-multi",
                "repeat1-no-multi-crlf",
                "repeat1-no-multi-crlf-cr",
                "repeat2",
                "repeat2-crlf",
                "repeat2-crlf-cr",
                "repeat2-no-multi",
                "repeat2-no-multi-crlf",
                "repeat2-no-multi-crlf-cr",
                "repeat3",
                "repeat3-crlf",
                "repeat3-crlf-cr",
                "repeat3-no-multi",
                "repeat3-no-multi-crlf",
                "repeat3-no-multi-crlf-cr",
                "repeat4",
                "repeat4-crlf",
                "repeat4-crlf-cr",
                "repeat4-no-multi",
                "repeat4-no-multi-crlf",
                "repeat4-no-multi-crlf-cr",
                "repeat5",
                "repeat5-crlf",
                "repeat5-crlf-cr",
                "repeat5-no-multi",
                "repeat5-no-multi-crlf",
                "repeat5-no-multi-crlf-cr",
                "repeat6",
                "repeat6-crlf",
                "repeat6-crlf-cr",
                "repeat6-no-multi",
                "repeat6-no-multi-crlf",
                "repeat6-no-multi-crlf-cr",
                "repeat7",
                "repeat7-crlf",
                "repeat7-crlf-cr",
                "repeat7-no-multi",
                "repeat7-no-multi-crlf",
                "repeat7-no-multi-crlf-cr",
                "repeat8",
                "repeat8-crlf",
                "repeat8-crlf-cr",
                "repeat8-no-multi",
                "repeat8-no-multi-crlf",
                "repeat8-no-multi-crlf-cr",
                "repeat9",
                "repeat9-crlf",
                "repeat9-crlf-cr",
                "repeat9-no-multi",
                "repeat9-no-multi-crlf",
                "repeat9-no-multi-crlf-cr",
                "repeat10",
                "repeat10-crlf",
                "repeat10-crlf-cr",
                "repeat10-no-multi",
                "repeat10-no-multi-crlf",
                "repeat10-no-multi-crlf-cr",
                "repeat11",
                "repeat11-crlf",
                "repeat11-crlf-cr",
                "repeat11-no-multi",
                "repeat11-no-multi-crlf",
                "repeat11-no-multi-crlf-cr",
                "repeat12",
                "repeat12-crlf",
                "repeat12-crlf-cr",
                "repeat12-no-multi",
                "repeat12-no-multi-crlf",
                "repeat12-no-multi-crlf-cr",
                "repeat13",
                "repeat13-crlf",
                "repeat13-crlf-cr",
                "repeat13-no-multi",
                "repeat13-no-multi-crlf",
                "repeat13-no-multi-crlf-cr",
                "repeat14",
                "repeat14-crlf",
                "repeat14-crlf-cr",
                "repeat14-no-multi",
                "repeat14-no-multi-crlf",
                "repeat14-no-multi-crlf-cr",
                "repeat15",
                "repeat15-crlf",
                "repeat15-crlf-cr",
                "repeat15-no-multi",
                "repeat15-no-multi-crlf",
                "repeat15-no-multi-crlf-cr",
                "repeat16",
                "repeat16-crlf",
                "repeat16-crlf-cr",
                "repeat16-no-multi",
                "repeat16-no-multi-crlf",
                "repeat16-no-multi-crlf-cr",
                "repeat17",
                "repeat17-crlf",
                "repeat17-crlf-cr",
                "repeat17-no-multi",
                "repeat17-no-multi-crlf",
                "repeat17-no-multi-crlf-cr",
                "repeat18",
                "repeat18-crlf",
                "repeat18-crlf-cr",
                "repeat18-no-multi",
                "repeat18-no-multi-crlf",
                "repeat18-no-multi-crlf-cr",
                "match-line-100",
                "match-line-100-crlf",
                "match-line-100-crlf-cr",
                "match-line-200",
                "match-line-200-crlf",
                "match-line-200-crlf-cr",
            ]),
            ("line-terminator.toml",
            [
                "nul",
                "dot-changes-with-line-terminator",
                "not-line-feed",
                "non-ascii",
                "carriage",
                "word-byte",
                "non-word-byte",
                "word-boundary",
                "word-boundary-at",
                "not-word-boundary-at",
            ]),
            ("bytes.toml",
            [
                "word-boundary-ascii",
                "word-boundary-ascii-not",
                "perl-word-ascii",
                "perl-decimal-ascii",
                "perl-whitespace-ascii",
                "case-one-ascii",
                "case-one-unicode",
                "case-class-simple-ascii",
                "case-class-ascii",
                "dotstar-prefix-ascii",
                "dotstar-prefix-unicode",
                "invalid-utf8-anchor-100",
                "invalid-utf8-anchor-200",
                "invalid-utf8-anchor-300",
                "negate-ascii",
                "null-bytes",
                "mixed-dot",
                "word-boundary-ascii-100",
                "word-boundary-ascii-200",
            ]),
            ("anchored.toml",
            [
                "greedy",
                "nongreedy",
                "word-boundary-unicode-01",
                "word-boundary-nounicode-01",
                "no-match-at-start",
                "no-match-at-start-bounds",
                "no-match-at-start-reverse-inner",
                "no-match-at-start-reverse-inner-bounds",
                "no-match-at-start-reverse-anchored",
                "no-match-at-start-reverse-anchored-bounds",
            ]),
            ("substring.toml",
            [
                "unicode-word-start",
                "unicode-word-end",
                "ascii-word-start",
                "ascii-word-end",
            ]),
            ("crlf.toml",
            [
                "basic",
                "start-end-non-empty",
                "start-end-empty",
                "start-end-before-after",
                "start-no-split",
                "start-no-split-adjacent",
                "start-no-split-adjacent-cr",
                "start-no-split-adjacent-lf",
                "end-no-split",
                "end-no-split-adjacent",
                "end-no-split-adjacent-cr",
                "end-no-split-adjacent-lf",
                "dot-no-crlf",
                "onepass-wrong-crlf-with-capture",
                "onepass-wrong-crlf-anchored",
            ]),
            ("regex-lite.toml",
            [
                "perl-class-decimal",
                "perl-class-space",
                "perl-class-word",
                "word-boundary",
                "word-boundary-negated",
                "empty-no-split-codepoint",
                "dot-always-matches-codepoint",
                "negated-class-always-matches-codepoint",
                "case-insensitive-is-ascii-only",
            ]),
            ("no-unicode.toml",
            [
                "invalid-utf8-literal1",
                "mixed",
                "case1",
                "case2",
                "negate1",
                "case4",
                "negate2",
                "dotstar-prefix1",
                "dotstar-prefix2",
                "null-bytes1",
                "word-ascii",
                "word-unicode",
                "decimal-ascii",
                "decimal-unicode",
                "space-ascii",
                "space-unicode",
                "iter1-bytes",
                "iter1-utf8",
                "iter2-bytes",
                "unanchored-invalid-utf8-match-100",
                "anchored-iter-empty-utf8",
            ]),
            ("regression.toml",
            [
                "invalid-regex-no-crash-100",
                "invalid-regex-no-crash-200",
                "invalid-regex-no-crash-300",
                "invalid-regex-no-crash-400",
                "unsorted-binary-search-100",
                "unsorted-binary-search-200",
                "negated-char-class-100",
                "negated-char-class-200",
                "ascii-word-underscore",
                "alt-in-alt-100",
                "alt-in-alt-200",
                "leftmost-first-prefix",
                "many-alternates",
                "word-boundary-alone-100",
                "word-boundary-alone-200",
                "word-boundary-ascii-no-capture",
                "word-boundary-ascii-capture",
                "end-not-word-boundary",
                "partial-anchor",
                "partial-anchor-alternate-begin",
                "partial-anchor-alternate-end",
                "lits-unambiguous-100",
                "lits-unambiguous-200",
                "negated-full-byte-range",
                "strange-anchor-non-complete-prefix",
                "strange-anchor-non-complete-suffix",
                "captures-after-dfa-premature-end-100",
                "captures-after-dfa-premature-end-200",
                "captures-after-dfa-premature-end-300",
                "captures-after-dfa-premature-end-400",
                "literal-panic",
                "empty-flag-expr",
                "flags-are-unset",
                "reverse-suffix-100",
                "reverse-suffix-200",
                "reverse-suffix-300",
                "stops",
                "stops-ascii",
                "adjacent-line-boundary-100",
                "adjacent-line-boundary-200",
                "anchored-prefix-100",
                "anchored-prefix-200",
                "anchored-prefix-300",
                "aho-corasick-100",
                "interior-anchor-capture",
                "ruff-whitespace-around-keywords",
                "fowler-basic154-unanchored",
                "impossible-branch",
                "captures-wrong-order",
                "missed-match",
                "regex-to-glob",
                "reverse-inner-plus-shorter-than-expected",
                "reverse-inner-short",
                "prefilter-with-aho-corasick-standard-semantics",
                "non-prefix-literal-quit-state",
                "hir-optimization-out-of-order-class",
            ]),
            ("set.toml",
            [
                "basic30",
                "basic40",
                "basic10-leftmost-first",
                "basic60-leftmost-first",
                "basic61-leftmost-first",
                "basic71",
                "basic80",
                "basic81",
                "basic82",
                "basic91",
                "basic110",
                "basic111",
                "basic120",
                "basic121",
                "basic122",
                "basic130",
                "empty10-leftmost-first",
                "empty11-leftmost-first",
                "empty20-leftmost-first",
                "empty21-leftmost-first",
                "empty30-leftmost-first",
                "empty31-leftmost-first",
                "empty40-leftmost-first",
                "nomatch10",
                "nomatch20",
                "nomatch30",
                "nomatch40",
                "caps-110",
                "caps-120",
                "caps-121",
            ]),
            ("word-boundary-special.toml",
            [
                "word-start-ascii-010",
                "word-start-ascii-020",
                "word-start-ascii-030",
                "word-start-ascii-040",
                "word-start-ascii-050",
                "word-start-ascii-060",
                "word-start-ascii-060-bounds",
                "word-start-ascii-070",
                "word-start-ascii-080",
                "word-start-ascii-090",
                "word-start-ascii-110",
                "word-end-ascii-010",
                "word-end-ascii-020",
                "word-end-ascii-030",
                "word-end-ascii-040",
                "word-end-ascii-050",
                "word-end-ascii-060",
                "word-end-ascii-060-bounds",
                "word-end-ascii-070",
                "word-end-ascii-080",
                "word-end-ascii-090",
                "word-end-ascii-110",
                "word-start-half-ascii-010",
                "word-start-half-ascii-020",
                "word-start-half-ascii-030",
                "word-start-half-ascii-040",
                "word-start-half-ascii-050",
                "word-start-half-ascii-060",
                "word-start-half-ascii-060-noutf8",
                "word-start-half-ascii-060-bounds",
                "word-start-half-ascii-070",
                "word-start-half-ascii-080",
                "word-start-half-ascii-090",
                "word-start-half-ascii-110",
                "word-end-half-ascii-010",
                "word-end-half-ascii-020",
                "word-end-half-ascii-030",
                "word-end-half-ascii-040",
                "word-end-half-ascii-050",
                "word-end-half-ascii-060",
                "word-end-half-ascii-060-bounds",
                "word-end-half-ascii-070",
                "word-end-half-ascii-080",
                "word-end-half-ascii-090",
                "word-end-half-ascii-110",
                "word-start-half-ascii-carriage",
                "word-start-half-ascii-linefeed",
                "word-start-half-ascii-customlineterm",
            ]),
        ];

    /// <summary>
    /// Verifies one supported <c>regex</c> 1.12.2 TOML corpus case.
    /// </summary>
    /// <param name="relativePath">The corpus TOML file.</param>
    /// <param name="name">The corpus case name.</param>
    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void CorpusCaseMatchesExpectedSpans(string relativePath, string name)
    {
        RegexCorpusCase testCase = RegexCorpusLoader.Load(relativePath, name);
        if (!testCase.Compiles)
        {
            AssertCompileFails(testCase.Patterns, testCase.LineTerminator, testCase.CaseInsensitive, testCase.Utf8, testCase.UnicodeClasses);
            return;
        }

        RegexAutomaton[] automata = CompileAll(testCase.Patterns, testCase.LineTerminator, testCase.CaseInsensitive, testCase.Utf8, testCase.UnicodeClasses);
        RegexMatch[] actual = FindAll(automata, testCase.Haystack, testCase.MatchLimit, testCase.BoundsStart, testCase.BoundsEnd, testCase.Anchored);

        Assert.True(
            MatchesEqual(testCase.ExpectedMatches, actual),
            relativePath + "::" + testCase.Name + " expected [" + FormatMatches(testCase.ExpectedMatches) + "] actual [" + FormatMatches(actual) + "]");
    }

    /// <summary>
    /// Gets every supported regex corpus case as xUnit data.
    /// </summary>
    /// <returns>The corpus case parameters.</returns>
    public static IEnumerable<object[]> CorpusCases()
    {
        for (int groupIndex = 0; groupIndex < CorpusGroups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = CorpusGroups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                yield return [relativePath, names[index]];
            }
        }
    }

    internal static string[] CorpusCaseKeys()
    {
        var keys = new List<string>();
        for (int groupIndex = 0; groupIndex < CorpusGroups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = CorpusGroups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                keys.Add(relativePath + "|" + names[index]);
            }
        }

        return keys.ToArray();
    }

    private static RegexAutomaton[] CompileAll(IReadOnlyList<byte[]> patterns, byte lineTerminator, bool caseInsensitive, bool utf8, bool unicodeClasses)
    {
        var automata = new RegexAutomaton[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            automata[index] = RegexAutomaton.Compile(patterns[index], caseInsensitive, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator, utf8, unicodeClasses);
        }

        return automata;
    }

    private static void AssertCompileFails(IReadOnlyList<byte[]> patterns, byte lineTerminator, bool caseInsensitive, bool utf8, bool unicodeClasses)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            Assert.Throws<FormatException>(() => RegexAutomaton.Compile(patterns[index], caseInsensitive, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator, utf8, unicodeClasses));
        }
    }

    private static RegexMatch[] FindAll(
        IReadOnlyList<RegexAutomaton> automata,
        byte[] haystack,
        int? matchLimit,
        int boundsStart,
        int boundsEnd,
        bool anchored)
    {
        var matches = new List<RegexMatch>();
        int startAt = boundsStart;
        int suppressedEmptyStart = -1;
        while (startAt <= boundsEnd)
        {
            if (matchLimit is int limit && matches.Count >= limit)
            {
                break;
            }

            RegexMatch? match = Find(automata, haystack, startAt, boundsEnd, anchored);
            if (!match.HasValue)
            {
                break;
            }

            if (match.Value.Length == 0 && match.Value.Start == suppressedEmptyStart)
            {
                startAt++;
                suppressedEmptyStart = -1;
                continue;
            }

            matches.Add(match.Value);
            if (match.Value.Length == 0)
            {
                startAt = match.Value.Start + 1;
                suppressedEmptyStart = -1;
            }
            else
            {
                startAt = match.Value.Start + match.Value.Length;
                suppressedEmptyStart = startAt;
            }
        }

        return matches.ToArray();
    }

    private static RegexMatch? Find(IReadOnlyList<RegexAutomaton> automata, byte[] haystack, int startAt, int boundsEnd, bool anchored)
    {
        RegexMatch? best = null;
        int bestPatternIndex = int.MaxValue;
        for (int index = 0; index < automata.Count; index++)
        {
            RegexMatch? match = automata[index].Find(haystack, startAt);
            if (!match.HasValue)
            {
                continue;
            }

            if ((anchored && match.Value.Start != startAt) ||
                match.Value.Start > boundsEnd ||
                match.Value.Start + match.Value.Length > boundsEnd)
            {
                continue;
            }

            if (!best.HasValue ||
                match.Value.Start < best.Value.Start ||
                match.Value.Start == best.Value.Start && index < bestPatternIndex)
            {
                best = match.Value;
                bestPatternIndex = index;
            }
        }

        return best;
    }

    private static bool MatchesEqual(IReadOnlyList<RegexMatch> expected, RegexMatch[] actual)
    {
        if (expected.Count != actual.Length)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!expected[index].Equals(actual[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatMatches(IReadOnlyList<RegexMatch> matches)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < matches.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            RegexMatch match = matches[index];
            builder.Append('[');
            builder.Append(match.Start);
            builder.Append(", ");
            builder.Append(match.Start + match.Length);
            builder.Append(']');
        }

        return builder.ToString();
    }
}
