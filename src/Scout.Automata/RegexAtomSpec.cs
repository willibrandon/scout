namespace Scout;

internal readonly struct RegexAtomSpec(RegexSyntaxKind kind, byte[] value)
{
    public RegexSyntaxKind Kind { get; } = kind;

    public byte[] Value { get; } = value;
}
