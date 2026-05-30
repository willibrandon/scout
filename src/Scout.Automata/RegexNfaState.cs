using System;

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
        int next,
        int alternative)
    {
        Kind = kind;
        AtomKind = atomKind;
        Value = value;
        CaseInsensitive = caseInsensitive;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
        Crlf = crlf;
        Next = next;
        Alternative = alternative;
    }

    public RegexNfaStateKind Kind { get; }

    public RegexSyntaxKind AtomKind { get; }

    public ReadOnlyMemory<byte> Value { get; }

    public bool CaseInsensitive { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public bool Crlf { get; }

    public int Next { get; }

    public int Alternative { get; }
}
