using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Runs Scout's regex engine against the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusTests
{
    /// <summary>
    /// Verifies supported subsets of <c>regex</c> 1.12.2's TOML corpus.
    /// </summary>
    [Fact]
    public void CorpusCasesMatchExpectedSpans()
    {
        (string RelativePath, string[] Names)[] groups =
        [
            ("misc.toml",
            [
                "ascii-literal",
                "ascii-literal-not",
                "anchor-start-end-line",
                "prefix-literal-match",
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
        ];

        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = groups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                RegexCorpusCase testCase = RegexCorpusLoader.Load(relativePath, names[index]);
                RegexAutomaton[] automata = CompileAll(testCase.Patterns, testCase.LineTerminator);
                RegexMatch[] actual = FindAll(automata, testCase.Haystack, testCase.MatchLimit, testCase.BoundsStart, testCase.BoundsEnd, testCase.Anchored);

                Assert.True(
                    MatchesEqual(testCase.ExpectedMatches, actual),
                    relativePath + "::" + testCase.Name + " expected [" + FormatMatches(testCase.ExpectedMatches) + "] actual [" + FormatMatches(actual) + "]");
            }
        }
    }

    private static RegexAutomaton[] CompileAll(IReadOnlyList<byte[]> patterns, byte lineTerminator)
    {
        var automata = new RegexAutomaton[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            automata[index] = RegexAutomaton.Compile(patterns[index], caseInsensitive: false, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator);
        }

        return automata;
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
