
namespace Scout;

internal sealed class RegexSearchPlan
{
    private readonly RegexAutomaton?[] automata;
    private readonly RegexClassSequenceAccelerator?[] accelerators;
    private readonly RegexLeadingLiteralCandidateAccelerator?[] leadingLiteralCandidateAccelerators;

    private RegexSearchPlan(
        RegexAutomaton?[] automata,
        RegexClassSequenceAccelerator?[] accelerators,
        RegexLeadingLiteralCandidateAccelerator?[] leadingLiteralCandidateAccelerators)
    {
        this.automata = automata;
        this.accelerators = accelerators;
        this.leadingLiteralCandidateAccelerators = leadingLiteralCandidateAccelerators;
    }

    public static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        return Create(needles, asciiCaseInsensitive, compileAutomata: true);
    }

    public static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive, bool compileAutomata)
    {
        RegexAutomaton?[]? automata = null;
        RegexClassSequenceAccelerator?[]? accelerators = null;
        RegexLeadingLiteralCandidateAccelerator?[]? leadingLiteralCandidateAccelerators = null;
        for (int index = 0; index < needles.Count; index++)
        {
            byte[] needle = needles[index];
            ArgumentNullException.ThrowIfNull(needle);
            bool hasAccelerator = false;
            if (RegexClassSequenceAccelerator.TryCompile(needle, out RegexClassSequenceAccelerator? accelerator))
            {
                accelerators ??= new RegexClassSequenceAccelerator?[needles.Count];
                accelerators[index] = accelerator;
                hasAccelerator = true;
            }

            if (!hasAccelerator &&
                RegexLeadingLiteralCandidateAccelerator.TryCompile(needle, asciiCaseInsensitive, out RegexLeadingLiteralCandidateAccelerator? leadingLiteralCandidateAccelerator))
            {
                leadingLiteralCandidateAccelerators ??= new RegexLeadingLiteralCandidateAccelerator?[needles.Count];
                leadingLiteralCandidateAccelerators[index] = leadingLiteralCandidateAccelerator;
                hasAccelerator = true;
            }

            if (hasAccelerator ||
                !compileAutomata ||
                !LiteralLineSearcher.ShouldPrecompileRegexAutomaton(needle, asciiCaseInsensitive))
            {
                continue;
            }

            automata ??= new RegexAutomaton?[needles.Count];
            automata[index] = RegexAutomaton.Compile(needle, asciiCaseInsensitive, multiLine: false, dotMatchesNewline: false);
        }

        return automata is null && accelerators is null && leadingLiteralCandidateAccelerators is null
            ? null
            : new RegexSearchPlan(
                automata ?? new RegexAutomaton?[needles.Count],
                accelerators ?? new RegexClassSequenceAccelerator?[needles.Count],
                leadingLiteralCandidateAccelerators ?? new RegexLeadingLiteralCandidateAccelerator?[needles.Count]);
    }

    public RegexAutomaton? GetAutomaton(int patternIndex)
    {
        return automata[patternIndex];
    }

    public RegexClassSequenceAccelerator? GetAccelerator(int patternIndex)
    {
        return accelerators[patternIndex];
    }

    public RegexLeadingLiteralCandidateAccelerator? GetLeadingLiteralCandidateAccelerator(int patternIndex)
    {
        return leadingLiteralCandidateAccelerators[patternIndex];
    }
}
