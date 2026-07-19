namespace Scout;

/// <summary>
/// Represents one immutable node in a parsed character-class set expression.
/// </summary>
internal sealed class RegexClassSetNode(
    RegexClassSetKind kind,
    int scalar = 0,
    int rangeEnd = 0,
    RegexLiteralKind literalKind = RegexLiteralKind.Verbatim,
    RegexLiteralKind rangeEndLiteralKind = RegexLiteralKind.Verbatim,
    RegexSyntaxKind atomKind = RegexSyntaxKind.Empty,
    RegexUnicodeProperty? unicodeProperty = null,
    bool negated = false,
    RegexCharacterClass? bracketed = null,
    IReadOnlyList<RegexClassSetNode>? items = null,
    RegexClassSetNode? left = null,
    RegexClassSetNode? right = null,
    RegexClassSetBinaryOperator binaryOperator = RegexClassSetBinaryOperator.Intersection)
{
    internal RegexClassSetKind Kind { get; } = kind;

    internal int Scalar { get; } = scalar;

    internal int RangeEnd { get; } = rangeEnd;

    internal RegexLiteralKind LiteralKind { get; } = literalKind;

    internal RegexLiteralKind RangeEndLiteralKind { get; } = rangeEndLiteralKind;

    internal RegexSyntaxKind AtomKind { get; } = atomKind;

    internal RegexUnicodeProperty? UnicodeProperty { get; } = unicodeProperty;

    internal bool Negated { get; } = negated;

    internal RegexCharacterClass? Bracketed { get; } = bracketed;

    internal IReadOnlyList<RegexClassSetNode> Items { get; } = items ?? [];

    internal RegexClassSetNode? Left { get; } = left;

    internal RegexClassSetNode? Right { get; } = right;

    internal RegexClassSetBinaryOperator BinaryOperator { get; } = binaryOperator;
}
