namespace Scout;

internal readonly struct PatternSetRequiredLiteralEntry
{
    public PatternSetRequiredLiteralEntry(int automatonIndex, byte[][] literals)
        : this(automatonIndex, literals, RegexPrefilter.RequiredLiteralLookBehind)
    {
    }

    public PatternSetRequiredLiteralEntry(int automatonIndex, byte[][] literals, int maxLookBehind)
        : this(automatonIndex, CreateRequiredLiterals(literals, maxLookBehind))
    {
    }

    public PatternSetRequiredLiteralEntry(int automatonIndex, PatternSetRequiredLiteral[] literals)
    {
        ArgumentNullException.ThrowIfNull(literals);

        AutomatonIndex = automatonIndex;
        Literals = literals;
    }

    public int AutomatonIndex { get; }

    public PatternSetRequiredLiteral[] Literals { get; }

    private static PatternSetRequiredLiteral[] CreateRequiredLiterals(byte[][] literals, int maxLookBehind)
    {
        ArgumentNullException.ThrowIfNull(literals);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLookBehind);

        var requiredLiterals = new PatternSetRequiredLiteral[literals.Length];
        for (int index = 0; index < literals.Length; index++)
        {
            requiredLiterals[index] = new PatternSetRequiredLiteral(literals[index], maxLookBehind);
        }

        return requiredLiterals;
    }
}
