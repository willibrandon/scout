using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Represents top-level alternatives in a regex expression.
/// </summary>
public sealed class RegexAlternationNode : RegexSyntaxNode
{
    internal RegexAlternationNode(IReadOnlyList<RegexSyntaxNode> alternatives, int position)
        : base(RegexSyntaxKind.Alternation, position)
    {
        Alternatives = alternatives;
    }

    /// <summary>
    /// Gets the alternative child nodes.
    /// </summary>
    public IReadOnlyList<RegexSyntaxNode> Alternatives { get; }
}
