namespace Scout;

/// <summary>
/// Represents a parsed regex syntax node.
/// </summary>
public abstract class RegexSyntaxNode
{
    private protected RegexSyntaxNode(RegexSyntaxKind kind, int position)
    {
        Kind = kind;
        Position = position;
    }

    /// <summary>
    /// Gets this node's syntax kind.
    /// </summary>
    public RegexSyntaxKind Kind { get; }

    /// <summary>
    /// Gets the byte offset where this node begins in the pattern.
    /// </summary>
    public int Position { get; }
}
