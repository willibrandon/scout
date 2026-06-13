namespace Scout;

internal readonly record struct RegexScalarRunAtom(
    RegexSyntaxKind Kind,
    byte[] Value,
    bool UnicodeLetterFastPath,
    bool CyrillicWordFastPath);
