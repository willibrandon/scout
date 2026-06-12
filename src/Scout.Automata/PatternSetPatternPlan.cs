namespace Scout;

internal readonly struct PatternSetPatternPlan
{
    public PatternSetPatternPlan(
        RegexSyntaxTree? tree,
        byte[][]? literalPatterns,
        byte[][]? requiredLiterals,
        int requiredLiteralLookBehind)
    {
        Tree = tree;
        LiteralPatterns = literalPatterns;
        RequiredLiterals = requiredLiterals;
        RequiredLiteralLookBehind = requiredLiteralLookBehind;
    }

    public RegexSyntaxTree? Tree { get; }

    public byte[][]? LiteralPatterns { get; }

    public byte[][]? RequiredLiterals { get; }

    public int RequiredLiteralLookBehind { get; }
}
