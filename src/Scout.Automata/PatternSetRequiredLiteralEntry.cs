namespace Scout;

internal readonly struct PatternSetRequiredLiteralEntry
{
    public PatternSetRequiredLiteralEntry(int automatonIndex, byte[][] literals)
    {
        AutomatonIndex = automatonIndex;
        Literals = literals;
    }

    public int AutomatonIndex { get; }

    public byte[][] Literals { get; }
}
