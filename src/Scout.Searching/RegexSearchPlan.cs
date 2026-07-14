
namespace Scout;

/// <summary>
/// Holds reusable regex engines and conservative accelerators for an ordered pattern set.
/// </summary>
/// <param name="compiledAutomata">The compiled authoritative automata.</param>
/// <param name="compiledAccelerators">The specialized class-sequence accelerators.</param>
/// <param name="compiledCandidateLineAccelerators">The conservative line-candidate accelerators.</param>
/// <param name="compiledLeadingLiteralCandidateAccelerators">The leading-literal candidate accelerators.</param>
/// <param name="compiledLiteralSetEngines">The exact literal-set engines.</param>
internal sealed class RegexSearchPlan(
    RegexAutomaton?[] compiledAutomata,
    RegexClassSequenceAccelerator?[] compiledAccelerators,
    RegexCandidateLineAccelerator?[] compiledCandidateLineAccelerators,
    RegexLeadingLiteralCandidateAccelerator?[] compiledLeadingLiteralCandidateAccelerators,
    RegexLiteralSetEngine?[] compiledLiteralSetEngines)
{
    private readonly RegexAutomaton?[] _automata = compiledAutomata;
    private readonly RegexClassSequenceAccelerator?[] _accelerators = compiledAccelerators;
    private readonly RegexCandidateLineAccelerator?[] _candidateLineAccelerators = compiledCandidateLineAccelerators;
    private readonly RegexLeadingLiteralCandidateAccelerator?[] _leadingLiteralCandidateAccelerators = compiledLeadingLiteralCandidateAccelerators;
    private readonly RegexLiteralSetEngine?[] _literalSetEngines = compiledLiteralSetEngines;

    /// <summary>
    /// Creates a plan and compiles authoritative automata when warranted.
    /// </summary>
    internal static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return Create(needles, asciiCaseInsensitive, compileAutomata: true);
    }

    /// <summary>
    /// Creates a plan for an ordered pattern set.
    /// </summary>
    internal static RegexSearchPlan? Create(
        IReadOnlyList<byte[]> needles,
        bool asciiCaseInsensitive,
        bool compileAutomata)
    {
        RegexAutomaton?[]? automata = null;
        RegexClassSequenceAccelerator?[]? accelerators = null;
        RegexCandidateLineAccelerator?[]? candidateLineAccelerators = null;
        RegexLeadingLiteralCandidateAccelerator?[]? leadingLiteralCandidateAccelerators = null;
        RegexLiteralSetEngine?[]? literalSetEngines = null;
        var literalSetOptions = new RegexCompileOptions(
            asciiCaseInsensitive,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        for (int index = 0; index < needles.Count; index++)
        {
            byte[] needle = needles[index];
            ArgumentNullException.ThrowIfNull(needle);
            bool hasAccelerator = false;
            bool requiresAutomatonMatchSpans = LiteralLineSearcher.RequiresAutomatonMatchSpans(needle);
            RegexCandidateLineAccelerator? candidateLineAccelerator = null;
            if (!requiresAutomatonMatchSpans &&
                RegexLiteralSetEngine.TryCreateLiteralAlternation(needle, literalSetOptions, out RegexLiteralSetEngine? literalSetEngine) &&
                literalSetEngine is not null)
            {
                literalSetEngines ??= new RegexLiteralSetEngine?[needles.Count];
                literalSetEngines[index] = literalSetEngine;
                hasAccelerator = true;
            }

            if (!requiresAutomatonMatchSpans &&
                RegexClassSequenceAccelerator.TryCompile(needle, out RegexClassSequenceAccelerator? accelerator))
            {
                accelerators ??= new RegexClassSequenceAccelerator?[needles.Count];
                accelerators[index] = accelerator;
                hasAccelerator = true;
            }

            if (!requiresAutomatonMatchSpans &&
                !hasAccelerator &&
                RegexLeadingLiteralCandidateAccelerator.TryCompile(needle, asciiCaseInsensitive, out RegexLeadingLiteralCandidateAccelerator? leadingLiteralCandidateAccelerator))
            {
                leadingLiteralCandidateAccelerators ??= new RegexLeadingLiteralCandidateAccelerator?[needles.Count];
                leadingLiteralCandidateAccelerators[index] = leadingLiteralCandidateAccelerator;
                hasAccelerator = true;
            }

            bool shouldPrecompileAutomaton = LiteralLineSearcher.ShouldPrecompileRegexAutomaton(
                needle,
                asciiCaseInsensitive);
            if (!requiresAutomatonMatchSpans &&
                !hasAccelerator &&
                RegexCandidateLineAccelerator.TryCompile(needle, asciiCaseInsensitive, out candidateLineAccelerator) &&
                candidateLineAccelerator is not null &&
                (candidateLineAccelerator.HasVerifier || !shouldPrecompileAutomaton))
            {
                candidateLineAccelerators ??= new RegexCandidateLineAccelerator?[needles.Count];
                candidateLineAccelerators[index] = candidateLineAccelerator;
                hasAccelerator = true;
            }

            if (hasAccelerator ||
                !compileAutomata ||
                !shouldPrecompileAutomaton)
            {
                continue;
            }

            automata ??= new RegexAutomaton?[needles.Count];
            var automaton = RegexAutomaton.Compile(needle, asciiCaseInsensitive, multiLine: false, dotMatchesNewline: false);
            automata[index] = automaton;
            if (!requiresAutomatonMatchSpans &&
                (candidateLineAccelerator is not null ||
                    RegexCandidateLineAccelerator.TryCompile(needle, asciiCaseInsensitive, out candidateLineAccelerator)))
            {
                candidateLineAccelerators ??= new RegexCandidateLineAccelerator?[needles.Count];
                candidateLineAccelerators[index] = candidateLineAccelerator;
            }
        }

        return automata is null && accelerators is null && candidateLineAccelerators is null && leadingLiteralCandidateAccelerators is null && literalSetEngines is null
            ? null
            : new RegexSearchPlan(
                automata ?? new RegexAutomaton?[needles.Count],
                accelerators ?? new RegexClassSequenceAccelerator?[needles.Count],
                candidateLineAccelerators ?? new RegexCandidateLineAccelerator?[needles.Count],
                leadingLiteralCandidateAccelerators ?? new RegexLeadingLiteralCandidateAccelerator?[needles.Count],
                literalSetEngines ?? new RegexLiteralSetEngine?[needles.Count]);
    }

    /// <summary>
    /// Gets the authoritative automaton for a pattern when one was compiled.
    /// </summary>
    internal RegexAutomaton? GetAutomaton(int patternIndex)
    {
        return _automata[patternIndex];
    }

    /// <summary>
    /// Gets the specialized class-sequence accelerator for a pattern.
    /// </summary>
    internal RegexClassSequenceAccelerator? GetAccelerator(int patternIndex)
    {
        return _accelerators[patternIndex];
    }

    /// <summary>
    /// Gets the conservative candidate-line accelerator for a pattern.
    /// </summary>
    internal RegexCandidateLineAccelerator? GetCandidateLineAccelerator(int patternIndex)
    {
        return _candidateLineAccelerators[patternIndex];
    }

    /// <summary>
    /// Gets the leading-literal candidate accelerator for a pattern.
    /// </summary>
    internal RegexLeadingLiteralCandidateAccelerator? GetLeadingLiteralCandidateAccelerator(int patternIndex)
    {
        return _leadingLiteralCandidateAccelerators[patternIndex];
    }

    /// <summary>
    /// Gets the exact literal-set engine for a pattern.
    /// </summary>
    internal RegexLiteralSetEngine? GetLiteralSetEngine(int patternIndex)
    {
        return _literalSetEngines[patternIndex];
    }
}
