namespace Scout;

internal readonly struct RegexLeadingClassLiteralBranch(
    RegexLeadingClassLiteralKind leadingKind,
    byte[] literal,
    RegexAtomSpec? trailingAtom)
{
    public RegexLeadingClassLiteralKind LeadingKind { get; } = leadingKind;

    public byte[] Literal { get; } = literal;

    public RegexAtomSpec? TrailingAtom { get; } = trailingAtom;
}
