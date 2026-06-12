namespace Scout;

internal sealed class RegexNfaState
{
    public RegexNfaState(
        RegexNfaStateKind kind,
        RegexSyntaxKind atomKind,
        ReadOnlyMemory<byte> value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses,
        int next,
        int alternative,
        int captureIndex = 0,
        RegexNfaSparseTransition[]? sparseTransitions = null)
    {
        Kind = kind;
        AtomKind = atomKind;
        Value = value;
        CaseInsensitive = caseInsensitive;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
        Crlf = crlf;
        LineTerminator = lineTerminator;
        Utf8 = utf8;
        UnicodeClasses = unicodeClasses;
        Next = next;
        Alternative = alternative;
        CaptureIndex = captureIndex;
        SparseTransitions = sparseTransitions ?? [];
    }

    public RegexNfaStateKind Kind { get; }

    public RegexSyntaxKind AtomKind { get; }

    public ReadOnlyMemory<byte> Value { get; }

    public bool CaseInsensitive { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public bool Crlf { get; }

    public byte LineTerminator { get; }

    public bool Utf8 { get; }

    public bool UnicodeClasses { get; }

    public int Next { get; }

    public int Alternative { get; }

    public int CaptureIndex { get; }

    public RegexNfaSparseTransition[] SparseTransitions { get; }

    public bool TryGetSparseTarget(byte value, out int next)
    {
        for (int index = 0; index < SparseTransitions.Length; index++)
        {
            RegexNfaSparseTransition transition = SparseTransitions[index];
            if (transition.Start <= value && value <= transition.End)
            {
                next = transition.Next;
                return true;
            }
        }

        next = -1;
        return false;
    }
}
