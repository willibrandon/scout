namespace Scout;

/// <summary>
/// Represents an empty regex expression.
/// </summary>
public sealed class RegexEmptyNode : RegexSyntaxNode
{
    internal RegexEmptyNode(int position)
        : base(RegexSyntaxKind.Empty, position)
    {
    }
}
