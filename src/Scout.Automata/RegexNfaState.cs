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
        byte lineTerminator,
        bool utf8,
        bool unicodeClasses,
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
        LineTerminator = lineTerminator;
        Utf8 = utf8;
        UnicodeClasses = unicodeClasses;
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

    public byte LineTerminator { get; }

    public bool Utf8 { get; }

    public bool UnicodeClasses { get; }

    public int Next { get; }

    public int Alternative { get; }
}
