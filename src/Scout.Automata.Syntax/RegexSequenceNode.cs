using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Represents concatenated regex syntax nodes.
/// </summary>
public sealed class RegexSequenceNode : RegexSyntaxNode
{
    internal RegexSequenceNode(IReadOnlyList<RegexSyntaxNode> nodes, int position)
        : base(RegexSyntaxKind.Sequence, position)
    {
        Nodes = nodes;
    }

    /// <summary>
    /// Gets the concatenated child nodes.
    /// </summary>
    public IReadOnlyList<RegexSyntaxNode> Nodes { get; }
}
