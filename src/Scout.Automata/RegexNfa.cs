using System.Collections.Generic;

namespace Scout;

internal sealed class RegexNfa
{
    public RegexNfa(IReadOnlyList<RegexNfaState> states, int startState, bool utf8)
    {
        States = states;
        StartState = startState;
        Utf8 = utf8;
    }

    public IReadOnlyList<RegexNfaState> States { get; }

    public int StartState { get; }

    public bool Utf8 { get; }
}
