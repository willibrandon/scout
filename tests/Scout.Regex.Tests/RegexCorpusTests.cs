using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Runs Scout's regex engine against the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
            ]),
            ("iter.toml",
            [
                "1",
                "2",
                "empty1",
                "empty2",
                "empty3",
            ]),
        ];

        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = groups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                RegexCorpusCase testCase = RegexCorpusLoader.Load(relativePath, names[index]);
                var automaton = RegexAutomaton.Compile(Utf8.GetBytes(testCase.Pattern));
                RegexMatch[] actual = FindAll(automaton, Utf8.GetBytes(testCase.Haystack), testCase.MatchLimit);

                Assert.True(
                    MatchesEqual(testCase.ExpectedMatches, actual),
                    relativePath + "::" + testCase.Name + " expected [" + FormatMatches(testCase.ExpectedMatches) + "] actual [" + FormatMatches(actual) + "]");
            }
        }
    }

    private static RegexMatch[] FindAll(RegexAutomaton automaton, byte[] haystack, int? matchLimit)
    {
        var matches = new List<RegexMatch>();
        int startAt = 0;
        while (startAt <= haystack.Length)
        {
            if (matchLimit is int limit && matches.Count >= limit)
            {
                break;
            }

            RegexMatch? match = automaton.Find(haystack, startAt);
            if (!match.HasValue)
            {
                break;
            }

            matches.Add(match.Value);
            int nextStart = match.Value.Start + Math.Max(match.Value.Length, 1);
            if (nextStart <= startAt)
            {
                nextStart = startAt + 1;
            }

            startAt = nextStart;
        }

        return matches.ToArray();
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
