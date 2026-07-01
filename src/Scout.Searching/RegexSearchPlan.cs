
namespace Scout;

internal sealed class RegexSearchPlan
{
    private readonly RegexAutomaton?[] automata;
    private readonly RegexClassSequenceAccelerator?[] accelerators;
    private readonly RegexCandidateLineAccelerator?[] candidateLineAccelerators;
    private readonly RegexLeadingLiteralCandidateAccelerator?[] leadingLiteralCandidateAccelerators;
    private readonly RegexLiteralSetEngine?[] literalSetEngines;

    private RegexSearchPlan(
        RegexAutomaton?[] automata,
        RegexClassSequenceAccelerator?[] accelerators,
        RegexCandidateLineAccelerator?[] candidateLineAccelerators,
        RegexLeadingLiteralCandidateAccelerator?[] leadingLiteralCandidateAccelerators,
        RegexLiteralSetEngine?[] literalSetEngines)
    {
        this.automata = automata;
        this.accelerators = accelerators;
        this.candidateLineAccelerators = candidateLineAccelerators;
        this.leadingLiteralCandidateAccelerators = leadingLiteralCandidateAccelerators;
        this.literalSetEngines = literalSetEngines;
    }

    public static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return Create(needles, asciiCaseInsensitive, compileAutomata: true);
    }

    public static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive, bool compileAutomata)
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

            if (!requiresAutomatonMatchSpans &&
                !hasAccelerator &&
                RegexCandidateLineAccelerator.TryCompile(needle, asciiCaseInsensitive, out candidateLineAccelerator) &&
                candidateLineAccelerator is { HasVerifier: true })
            {
                candidateLineAccelerators ??= new RegexCandidateLineAccelerator?[needles.Count];
                candidateLineAccelerators[index] = candidateLineAccelerator;
                hasAccelerator = true;
            }

            bool shouldPrecompileAutomaton = LiteralLineSearcher.ShouldPrecompileRegexAutomaton(needle, asciiCaseInsensitive);
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

    public RegexAutomaton? GetAutomaton(int patternIndex)
    {
        return automata[patternIndex];
    }

    public RegexClassSequenceAccelerator? GetAccelerator(int patternIndex)
    {
        return accelerators[patternIndex];
    }

    public RegexCandidateLineAccelerator? GetCandidateLineAccelerator(int patternIndex)
    {
        return candidateLineAccelerators[patternIndex];
    }

    public RegexLeadingLiteralCandidateAccelerator? GetLeadingLiteralCandidateAccelerator(int patternIndex)
    {
        return leadingLiteralCandidateAccelerators[patternIndex];
    }

    public RegexLiteralSetEngine? GetLiteralSetEngine(int patternIndex)
    {
        return literalSetEngines[patternIndex];
    }
}
