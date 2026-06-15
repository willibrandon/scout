namespace Scout;

internal readonly struct PatternSetRequiredAutomaton
{
    public PatternSetRequiredAutomaton(int automatonIndex, int maxLookBehind)
    {
        AutomatonIndex = automatonIndex;
        MaxLookBehind = maxLookBehind;
    }

    public int AutomatonIndex { get; }

    public int MaxLookBehind { get; }
}
