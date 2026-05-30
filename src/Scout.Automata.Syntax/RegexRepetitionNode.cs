namespace Scout;

/// <summary>
/// Represents a quantified regex expression.
/// </summary>
public sealed class RegexRepetitionNode : RegexSyntaxNode
{
    internal RegexRepetitionNode(RegexSyntaxNode child, int minimum, int? maximum, bool lazy, int position)
        : base(RegexSyntaxKind.Repetition, position)
    {
        Child = child;
        Minimum = minimum;
        Maximum = maximum;
        Lazy = lazy;
    }

    /// <summary>
    /// Gets the repeated child expression.
    /// </summary>
    public RegexSyntaxNode Child { get; }

    /// <summary>
    /// Gets the minimum repetition count.
    /// </summary>
    public int Minimum { get; }

    /// <summary>
    /// Gets the maximum repetition count, or <see langword="null" /> when unbounded.
    /// </summary>
    public int? Maximum { get; }

    /// <summary>
    /// Gets a value indicating whether this repetition is lazy.
    /// </summary>
    public bool Lazy { get; }
}
