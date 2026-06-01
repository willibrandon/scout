using System;
using System.Collections.Generic;

namespace Scout;

internal sealed class RegexSearchPlan
{
    private readonly RegexAutomaton?[] automata;
    private readonly RegexClassSequenceAccelerator?[] accelerators;

    private RegexSearchPlan(RegexAutomaton?[] automata, RegexClassSequenceAccelerator?[] accelerators)
    {
        this.automata = automata;
        this.accelerators = accelerators;
    }

    public static RegexSearchPlan? Create(IReadOnlyList<byte[]> needles, bool asciiCaseInsensitive)
    {
        RegexAutomaton?[]? automata = null;
        RegexClassSequenceAccelerator?[]? accelerators = null;
        for (int index = 0; index < needles.Count; index++)
        {
            byte[] needle = needles[index];
            ArgumentNullException.ThrowIfNull(needle);
            if (RegexClassSequenceAccelerator.TryCompile(needle, out RegexClassSequenceAccelerator? accelerator))
            {
                accelerators ??= new RegexClassSequenceAccelerator?[needles.Count];
                accelerators[index] = accelerator;
            }

            if (!LiteralLineSearcher.ShouldPrecompileRegexAutomaton(needle, asciiCaseInsensitive))
            {
                continue;
            }

            automata ??= new RegexAutomaton?[needles.Count];
            automata[index] = RegexAutomaton.Compile(needle, asciiCaseInsensitive, multiLine: false, dotMatchesNewline: false);
        }

        return automata is null && accelerators is null
            ? null
            : new RegexSearchPlan(
                automata ?? new RegexAutomaton?[needles.Count],
                accelerators ?? new RegexClassSequenceAccelerator?[needles.Count]);
    }

    public RegexAutomaton? GetAutomaton(int patternIndex)
    {
        return automata[patternIndex];
    }

    public RegexClassSequenceAccelerator? GetAccelerator(int patternIndex)
    {
        return accelerators[patternIndex];
    }
}
