using System.Text;

namespace Scout;

/// <summary>
/// Verifies streamed PikeVM search semantics independently of specialized regex engines.
/// </summary>
public sealed class PikeVmTests()
{
    /// <summary>
    /// Verifies streamed candidates agree with sequential anchored matching across ASCII,
    /// variable-width UTF-8, acyclic closures, cyclic closures, and positional predicates.
    /// </summary>
    [Fact]
    public void StreamedFindMatchesSequentialAnchoredSearch()
    {
        string[] patterns =
        [
            "(?:a?)*b",
            "(?:ab|a)+?b",
            "(?:a|aa)*b",
            "(?m)^abcdefghijklmnopqrstuvwxyz0123456789$",
            "(?:ab|ac)d",
            "a(?:b|)c",
            "(?:a*?|aa)b",
            "(?i)[\\w.-]{0,5}?needle(?:=|:)[a-z]{2}(?:;|$)",
        ];
        byte[][] haystacks =
        [
            [],
            "b"u8.ToArray(),
            "aaab"u8.ToArray(),
            "aaac"u8.ToArray(),
            "ab"u8.ToArray(),
            "aab"u8.ToArray(),
            "abc"u8.ToArray(),
            "acd"u8.ToArray(),
            "xx\nabcdefghijklmnopqrstuvwxyz0123456789\n"u8.ToArray(),
            "!needle:ok"u8.ToArray(),
            Encoding.UTF8.GetBytes("δneedle=ok;"),
            [0xFF, (byte)'a', (byte)'b'],
        ];

        for (int patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
        {
            RegexNfa nfa = CompileNfa(patterns[patternIndex]);
            var streamedVm = new PikeVm(nfa);
            var anchoredVm = new PikeVm(nfa);
            for (int haystackIndex = 0; haystackIndex < haystacks.Length; haystackIndex++)
            {
                byte[] haystack = haystacks[haystackIndex];
                for (int startAt = 0; startAt <= haystack.Length; startAt++)
                {
                    RegexMatch? expected = FindSequentially(anchoredVm, nfa, haystack, startAt);
                    var candidates = RegexCandidateStartEnumerator.Every(
                        haystack,
                        startAt,
                        haystack.Length,
                        nfa.Utf8,
                        startPredicate: null);

                    RegexMatch? actual = streamedVm.Find(haystack, ref candidates);

                    Assert.True(
                        expected == actual,
                        $"Pattern '{patterns[patternIndex]}', haystack {Convert.ToHexString(haystack)}, start {startAt}: expected {expected}, actual {actual}.");
                }
            }
        }
    }

    private static RegexNfa CompileNfa(string pattern)
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        return RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
    }

    private static RegexMatch? FindSequentially(
        PikeVm vm,
        RegexNfa nfa,
        ReadOnlySpan<byte> haystack,
        int startAt)
    {
        for (int start = startAt; start <= haystack.Length; start++)
        {
            if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
            {
                continue;
            }

            if (vm.TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }
}
