using System.Text;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Verifies compilation of the representative Gitleaks assignment rule from issue #49.
/// </summary>
public sealed class GitleaksRuleCompilationTests()
{
    private const long CompileAllocationLimit = 320 * 1024;
    private const ulong EstimatedNfaStateBytes = 256;
    private const string Pattern = """(?i)[\w.-]{0,50}?(?:coinbase)(?:[ \t\w.-]{0,20})[\s'"]{0,3}(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)[\x60'"\s=]{0,5}([a-z0-9_-]{64})(?:[\x60'"\s;]|\\[nr]|$)""";

    /// <summary>
    /// Verifies every public engine mode preserves the rule's match and capture semantics.
    /// </summary>
    /// <param name="engineMode">The public engine mode under test.</param>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void CompilesAndMatchesAcrossEngineModes(ByteRegexEngineMode engineMode)
    {
        string secret = new('a', 64);
        byte[] matching = Encoding.UTF8.GetBytes($"coinbase = {secret}");
        byte[] tooShort = Encoding.UTF8.GetBytes($"coinbase = {secret[..^1]}");
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = engineMode });

        ByteRegexCaptures? captures = regex.FindCaptures(matching);

        Assert.NotNull(captures);
        Assert.Equal(new ByteRegexMatch(0, matching.Length), captures.Match);
        Assert.Equal(new ByteRegexMatch(11, 64), captures.GetGroup(1));
        Assert.True(captures.GetGroup(1)!.Value.Value(matching).SequenceEqual(Encoding.ASCII.GetBytes(secret)));
        Assert.Null(regex.Find(tooShort));
    }

    /// <summary>
    /// Verifies the repeated Unicode class and case-folded secret class retain scalar semantics.
    /// </summary>
    /// <param name="engineMode">The public engine mode under test.</param>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void MatchesUnicodeAndCaseFoldedInputAcrossEngineModes(ByteRegexEngineMode engineMode)
    {
        const string Prefix = "Kname.";
        string secret = new('A', 64);
        byte[] matching = Encoding.UTF8.GetBytes($"{Prefix}coinbase = {secret}");
        var regex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { EngineMode = engineMode });

        ByteRegexCaptures? captures = regex.FindCaptures(matching);

        Assert.NotNull(captures);
        Assert.Equal(new ByteRegexMatch(0, matching.Length), captures.Match);
        int secretStart = Encoding.UTF8.GetByteCount($"{Prefix}coinbase = ");
        Assert.Equal(new ByteRegexMatch(secretStart, 64), captures.GetGroup(1));
    }

    /// <summary>
    /// Verifies repeated compilation stays within a bounded allocation budget in every public mode.
    /// </summary>
    /// <param name="engineMode">The public engine mode under test.</param>
    [Theory]
    [InlineData(ByteRegexEngineMode.Optimized)]
    [InlineData(ByteRegexEngineMode.General)]
    [InlineData(ByteRegexEngineMode.AutomataOnly)]
    public void CompileAllocationsStayBoundedAcrossEngineModes(ByteRegexEngineMode engineMode)
    {
        var options = new ByteRegexOptions { EngineMode = engineMode };
        _ = ByteRegex.Compile(Pattern, options);
        _ = ByteRegex.Compile(Pattern, options);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var regex = ByteRegex.Compile(Pattern, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(regex);
        Assert.True(
            allocated <= CompileAllocationLimit,
            $"Expected compilation to allocate at most {CompileAllocationLimit} bytes, but it allocated {allocated} bytes.");
    }

    /// <summary>
    /// Verifies bounded copies of one authoritative class share its canonical scalar payload.
    /// </summary>
    [Fact]
    public void BoundedAuthoritativeClassCopiesShareScalarRanges()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?i)[\w.-]{0,50}?"u8);
        var options = new RegexCompileOptions(
            caseInsensitive: false,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: true,
            unicodeClasses: true);
        RegexNfa nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
        RegexScalarRange[][] payloads = nfa.States
            .Where(static state => state.ScalarRanges is not null)
            .Select(static state => state.ScalarRanges!)
            .ToArray();

        Assert.Equal(50, payloads.Length);
        Assert.All(payloads, payload => Assert.Same(payloads[0], payload));
    }

    /// <summary>
    /// Verifies scalar-plan reuse remains separated by every option that changes class semantics.
    /// </summary>
    [Fact]
    public void ScalarPlanCacheSeparatesSemanticOptions()
    {
        var cache = new RegexScalarAtomPlanCache();
        RegexAtomNode caseAtom = Assert.IsType<RegexAtomNode>(
            RegexSyntaxParser.Parse("[k]"u8).Root);
        RegexCompileOptions caseSensitive = CreateOptions(caseInsensitive: false);
        RegexCompileOptions caseInsensitive = CreateOptions(caseInsensitive: true);

        Assert.True(cache.TryGet(caseAtom, caseSensitive, out RegexScalarAtomPlan? sensitivePlan));
        Assert.True(cache.TryGet(caseAtom, caseInsensitive, out RegexScalarAtomPlan? insensitivePlan));
        Assert.True(cache.TryGet(caseAtom, caseSensitive, out RegexScalarAtomPlan? cachedSensitivePlan));
        Assert.Same(sensitivePlan, cachedSensitivePlan);
        Assert.NotSame(sensitivePlan, insensitivePlan);
        Assert.False(ContainsScalar(sensitivePlan!.Ranges, 0x212A));
        Assert.True(ContainsScalar(insensitivePlan!.Ranges, 0x212A));

        RegexAtomNode wordAtom = Assert.IsType<RegexAtomNode>(
            RegexSyntaxParser.Parse(@"[\w]"u8).Root);
        RegexCompileOptions unicode = CreateOptions(utf8: true, unicodeClasses: true);
        RegexCompileOptions ascii = CreateOptions(utf8: false, unicodeClasses: false);

        Assert.True(cache.TryGet(wordAtom, unicode, out RegexScalarAtomPlan? unicodePlan));
        Assert.True(cache.TryGet(wordAtom, ascii, out RegexScalarAtomPlan? asciiPlan));
        Assert.NotSame(unicodePlan, asciiPlan);
        Assert.True(ContainsScalar(unicodePlan!.Ranges, 0x03B1));
        Assert.False(ContainsScalar(asciiPlan!.Ranges, 0x03B1));

        RegexAtomNode whitespaceAtom = Assert.IsType<RegexAtomNode>(
            RegexSyntaxParser.Parse(@"[\s]"u8).Root);
        RegexCompileOptions unrestricted = CreateOptions();
        RegexCompileOptions excludeLf = CreateOptions(
            excludeLineTerminators: true,
            excludeCrLf: false);
        RegexCompileOptions excludeCrLf = CreateOptions(
            excludeLineTerminators: true,
            excludeCrLf: true);

        Assert.True(cache.TryGet(whitespaceAtom, unrestricted, out RegexScalarAtomPlan? unrestrictedPlan));
        Assert.True(cache.TryGet(whitespaceAtom, excludeLf, out RegexScalarAtomPlan? excludeLfPlan));
        Assert.True(cache.TryGet(whitespaceAtom, excludeCrLf, out RegexScalarAtomPlan? excludeCrLfPlan));
        Assert.NotSame(unrestrictedPlan, excludeLfPlan);
        Assert.NotSame(excludeLfPlan, excludeCrLfPlan);
        Assert.True(ContainsScalar(unrestrictedPlan!.Ranges, '\n'));
        Assert.False(ContainsScalar(excludeLfPlan!.Ranges, '\n'));
        Assert.True(ContainsScalar(excludeLfPlan.Ranges, '\r'));
        Assert.False(ContainsScalar(excludeCrLfPlan!.Ranges, '\n'));
        Assert.False(ContainsScalar(excludeCrLfPlan.Ranges, '\r'));
    }

    /// <summary>
    /// Verifies shared plans preserve Unicode properties, scalar escapes, negation, and class-set algebra.
    /// </summary>
    /// <param name="pattern">The repeated authoritative pattern.</param>
    /// <param name="haystack">The UTF-8 haystack.</param>
    /// <param name="expectedLength">The expected byte length.</param>
    [Theory]
    [InlineData("(?i:[A-Z--AEIOU]{2,4})", "bcdf", 4)]
    [InlineData(@"(?:[\w&&\p{Latin}]){2,4}", "abδ", 2)]
    [InlineData(@"(?:\p{Greek}){2,4}", "αβ!", 4)]
    [InlineData(@"(?:\x{100}){2,4}", "ĀĀ!", 4)]
    [InlineData("(?:[^a]){2,4}", "βγa", 4)]
    [InlineData("(?:[a-f~~d-z]){2,4}", "gh!", 2)]
    public void RepeatedAuthoritativeAtomPlansPreserveSemantics(
        string pattern,
        string haystack,
        int expectedLength)
    {
        RegexMatch? match = RegexAutomaton.Compile(Encoding.UTF8.GetBytes(pattern))
            .Find(Encoding.UTF8.GetBytes(haystack));

        Assert.Equal(new RegexMatch(0, expectedLength), match);
    }

    /// <summary>
    /// Verifies retained-NFA budgets count one shared scalar payload for repeated states.
    /// </summary>
    [Fact]
    public void RetainedBudgetCountsSharedScalarPayloadOnce()
    {
        RegexSyntaxTree tree = RegexSyntaxParser.Parse(@"(?i)[\w.-]{0,50}?"u8);
        RegexCompileOptions options = CreateOptions(caseInsensitive: false);
        RegexNfa nfa = RegexNfaCompiler.CompileWithCompactScalarAtoms(
            tree.Root,
            options,
            utf8ByteTrieCache: null);
        ulong retainedBytes = RegexNfaConstructionBudget.EstimateRetainedBytes(nfa);
        var scalarRangePayloads = new HashSet<RegexScalarRange[]>(
            ReferenceEqualityComparer.Instance);
        ulong expectedBytes = 0;
        for (int index = 0; index < nfa.States.Count; index++)
        {
            RegexNfaState state = nfa.States[index];
            if (state.ScalarRanges is not null)
            {
                scalarRangePayloads.Add(state.ScalarRanges);
            }

            expectedBytes += EstimatedNfaStateBytes +
                (ulong)state.Value.Length +
                RegexNfaConstructionBudget.EstimateSparsePayloadBytes(
                    state.SparseTransitions.Length);
        }

        RegexScalarRange[] sharedRanges = Assert.Single(scalarRangePayloads);
        expectedBytes += (ulong)sharedRanges.Length * (sizeof(int) * 2);
        var exactBudget = new RegexNfaConstructionBudget(retainedBytes);
        var insufficientBudget = new RegexNfaConstructionBudget(retainedBytes - 1);

        exactBudget.ReserveRetainedNfa(nfa);

        Assert.Equal(expectedBytes, retainedBytes);
        Assert.Equal(retainedBytes, exactBudget.UsedBytes);
        Assert.Throws<InsufficientMemoryException>(() => insufficientBudget.ReserveRetainedNfa(nfa));
        Assert.Equal(0UL, insufficientBudget.UsedBytes);
    }

    private static RegexCompileOptions CreateOptions(
        bool caseInsensitive = false,
        bool utf8 = true,
        bool unicodeClasses = true,
        bool excludeLineTerminators = false,
        bool excludeCrLf = false)
    {
        return new RegexCompileOptions(
            caseInsensitive,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false,
            utf8: utf8,
            unicodeClasses: unicodeClasses,
            excludeLineTerminators: excludeLineTerminators,
            excludeCrLf: excludeCrLf,
            excludedLineTerminator: (byte)'\n');
    }

    private static bool ContainsScalar(RegexScalarRange[] ranges, int scalar)
    {
        for (int index = 0; index < ranges.Length; index++)
        {
            RegexScalarRange range = ranges[index];
            if (scalar < range.Start)
            {
                return false;
            }

            if (scalar <= range.End)
            {
                return true;
            }
        }

        return false;
    }
}
