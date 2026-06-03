
namespace Scout;

/// <summary>
/// Represents a single regex atom such as a literal, class, anchor, or boundary.
/// </summary>
public sealed class RegexAtomNode : RegexSyntaxNode
{
    internal RegexAtomNode(RegexSyntaxKind kind, ReadOnlyMemory<byte> value, int position)
        : base(kind, position)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the atom payload, when the atom carries pattern bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }
}
