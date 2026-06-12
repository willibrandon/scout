namespace Scout;

internal readonly struct PatternSetPatternPlan
{
    public PatternSetPatternPlan(
        RegexSyntaxTree? tree,
        byte[][]? literalPatterns,
        byte[][]? requiredLiterals,
        int requiredLiteralLookBehind)
        : this(tree, literalPatterns, boundaryLiteralPatterns: null, requiredLiterals, requiredLiteralLookBehind)
    {
    }

    public PatternSetPatternPlan(
        RegexSyntaxTree? tree,
        byte[][]? literalPatterns,
        byte[][]? boundaryLiteralPatterns,
        byte[][]? requiredLiterals,
        int requiredLiteralLookBehind)
    {
        Tree = tree;
        LiteralPatterns = literalPatterns;
        BoundaryLiteralPatterns = boundaryLiteralPatterns;
        RequiredLiterals = requiredLiterals;
        RequiredLiteralLookBehind = requiredLiteralLookBehind;
    }

    public RegexSyntaxTree? Tree { get; }

    public byte[][]? LiteralPatterns { get; }

    public byte[][]? BoundaryLiteralPatterns { get; }

    public byte[][]? RequiredLiterals { get; }

    public int RequiredLiteralLookBehind { get; }
}
