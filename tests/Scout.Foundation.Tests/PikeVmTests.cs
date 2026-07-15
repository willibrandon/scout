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
            Encoding.UTF8.GetBytes("\u03B4needle=ok;"),
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

    /// <summary>
    /// Verifies speculative ASCII execution restarts at the untouched candidate stream when a
    /// live consumer reaches a non-ASCII scalar.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="haystackText">The UTF-8 haystack.</param>
    /// <param name="startAt">The first permitted candidate start.</param>
    [Theory]
    [InlineData("a(?:.*|)", "a\u03B4", 0)]
    [InlineData("needle", "\u03B4needle", 0)]
    [InlineData("a", "a\u03B4", 0)]
    [InlineData("a+", "aaa\u03B4aaa", 3)]
    [InlineData("a+", "aaa\u03B4aaa", 4)]
    public void SpeculativeAsciiFindMatchesSequentialSearch(
        string pattern,
        string haystackText,
        int startAt)
    {
        RegexNfa nfa = CompileNfa(pattern);
        byte[] haystack = Encoding.UTF8.GetBytes(haystackText);
        var expectedVm = new PikeVm(nfa);
        var actualVm = new PikeVm(nfa);
        RegexMatch? expected = FindSequentially(expectedVm, nfa, haystack, startAt);
        var candidates = RegexCandidateStartEnumerator.Every(
            haystack,
            startAt,
            haystack.Length,
            nfa.Utf8,
            startPredicate: null);

        RegexMatch? actual = actualVm.Find(haystack, ref candidates);

        Assert.Equal(expected, actual);
        if (pattern == "a(?:.*|)")
        {
            Assert.Equal(new RegexMatch(0, 3), actual);
        }
    }

    /// <summary>
    /// Verifies repeated streamed searches preserve matches before, between, and after non-ASCII scalars.
    /// </summary>
    [Fact]
    public void SpeculativeAsciiIterationHandlesNonAsciiAroundMatches()
    {
        RegexNfa nfa = CompileNfa("a+");
        byte[] haystack = Encoding.UTF8.GetBytes("\u03B4aa\u03B4aaa\u03B4a\u03B4");
        var vm = new PikeVm(nfa);
        var matches = new List<RegexMatch>();
        int startAt = 0;
        while (startAt <= haystack.Length)
        {
            var candidates = RegexCandidateStartEnumerator.Every(
                haystack,
                startAt,
                haystack.Length,
                nfa.Utf8,
                startPredicate: null);
            RegexMatch? match = vm.Find(haystack, ref candidates);
            if (!match.HasValue)
            {
                break;
            }

            matches.Add(match.Value);
            startAt = match.Value.End;
        }

        Assert.Equal(
            [new RegexMatch(2, 2), new RegexMatch(6, 3), new RegexMatch(11, 1)],
            matches);
    }

    /// <summary>
    /// Verifies speculative ASCII execution preserves structural candidate-start filtering around UTF-8 input.
    /// </summary>
    [Fact]
    public void SpeculativeAsciiFindPreservesCandidateStartFiltering()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse("(?m)^a"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: false,
            unicodeClasses: false);
        RegexNfa nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
        Assert.True(RegexStartPredicate.TryCreate(tree.Root, options, out RegexStartPredicate? predicate));
        Assert.NotNull(predicate);
        byte[] haystack = Encoding.UTF8.GetBytes("x\u03B4a\na");
        var expectedVm = new PikeVm(nfa);
        var actualVm = new PikeVm(nfa);
        RegexMatch? expected = FindSequentially(expectedVm, nfa, haystack, startAt: 0, predicate: predicate);
        var candidates = RegexCandidateStartEnumerator.Every(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            utf8: nfa.Utf8,
            startPredicate: predicate);

        RegexMatch? actual = actualVm.Find(haystack, ref candidates);

        Assert.Equal(new RegexMatch(5, 1), expected);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies non-ASCII search preserves active required-literal ranges instead of sharing
    /// their mutable scratch with a speculative value copy.
    /// </summary>
    [Fact]
    public void FindPreservesBufferedRequiredLiteralRangesAcrossNonAsciiInput()
    {
        const string pattern = "(?:Z.{99}|Q)(?:needle).$";
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(Encoding.UTF8.GetBytes(pattern));
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        Assert.NotNull(prefilter);
        Assert.Equal(RegexPrefilterKind.RequiredLiteral, prefilter.Kind);
        Assert.True(prefilter.UsesRequiredLiteralPrefixGate);
        RegexNfa nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
        Span<long> requiredRangeBuffer =
            stackalloc long[RegexCandidateStartEnumerator.RequiredLiteralRangeBufferLength];

        byte[] haystack = Enumerable.Repeat((byte)'x', 248).ToArray();
        "Qneedle"u8.CopyTo(haystack.AsSpan(99));
        "Qneedle"u8.CopyTo(haystack.AsSpan(119));
        haystack[140] = (byte)'Z';
        "needle"u8.CopyTo(haystack.AsSpan(240));
        "\u00E9"u8.CopyTo(haystack.AsSpan(246));
        var candidates = RegexCandidateStartEnumerator.RequiredLiteralRanges(
            haystack,
            startAt: 0,
            maxStart: haystack.Length,
            nfa.Utf8,
            prefilter,
            requiredRangeBuffer);

        Assert.True(candidates.MoveNext(out int firstCandidate));
        Assert.Equal(99, firstCandidate);
        Assert.True(candidates.HasBufferedRequiredRanges);

        RegexMatch? match = new PikeVm(nfa).Find(haystack, ref candidates);

        Assert.Equal(new RegexMatch(140, 108), match);
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
        int startAt,
        RegexStartPredicate? predicate = null)
    {
        for (int start = startAt; start <= haystack.Length; start++)
        {
            if (nfa.Utf8 && !RegexByteClass.IsUtf8Boundary(haystack, start))
            {
                continue;
            }

            if (predicate is not null && !predicate.CanStartAt(haystack, start))
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
