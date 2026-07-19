
namespace Scout;

/// <summary>
/// Represents a single regex atom such as a literal, class, anchor, or boundary.
/// </summary>
public sealed class RegexAtomNode : RegexSyntaxNode
{
    internal RegexAtomNode(
        RegexSyntaxKind kind,
        ReadOnlyMemory<byte> value,
        int position,
        int scalar = -1,
        RegexLiteralKind literalKind = RegexLiteralKind.Verbatim,
        RegexCharacterClass? characterClass = null,
        RegexUnicodeProperty? unicodeProperty = null)
        : base(kind, position)
    {
        Value = value;
        Scalar = scalar;
        LiteralKind = literalKind;
        CharacterClass = characterClass;
        UnicodeProperty = unicodeProperty;
    }

    /// <summary>
    /// Gets the atom payload, when the atom carries pattern bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }

    internal int Scalar { get; }

    internal RegexLiteralKind LiteralKind { get; }

    internal RegexCharacterClass? CharacterClass { get; }

    internal RegexUnicodeProperty? UnicodeProperty { get; }
}
