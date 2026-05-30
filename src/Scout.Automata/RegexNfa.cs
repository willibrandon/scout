using System.Collections.Generic;

namespace Scout;

internal sealed class RegexNfa
{
    public RegexNfa(IReadOnlyList<RegexNfaState> states, int startState)
    {
        States = states;
        StartState = startState;
    }

    public IReadOnlyList<RegexNfaState> States { get; }

    public int StartState { get; }
}
