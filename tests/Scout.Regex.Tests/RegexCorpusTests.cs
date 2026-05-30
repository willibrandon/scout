using System.Text;

namespace Scout;

/// <summary>
/// Runs Scout's regex engine against the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Verifies an initial supported subset of <c>regex</c> 1.12.2's TOML corpus.
    /// </summary>
    [Fact]
    public void MiscCorpusCasesMatchExpectedSpans()
    {
        string[] names =
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
            "suffix-600",
        ];

        for (int index = 0; index < names.Length; index++)
        {
            RegexCorpusCase testCase = RegexCorpusLoader.Load("misc.toml", names[index]);
            var automaton = RegexAutomaton.Compile(Utf8.GetBytes(testCase.Pattern));
            RegexMatch? actual = automaton.Find(Utf8.GetBytes(testCase.Haystack));

            Assert.Equal(testCase.ExpectedMatch, actual);
        }
    }
}
