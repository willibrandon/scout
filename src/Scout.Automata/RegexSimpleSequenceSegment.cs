namespace Scout;

internal readonly struct RegexSimpleSequenceSegment
{
    public RegexSimpleSequenceSegment(
        RegexSyntaxKind kind,
        byte[] value,
        bool caseInsensitive,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf,
        byte lineTerminator,
        int minimum,
        int? maximum,
        bool lazy)
    {
        Kind = kind;
        Value = value;
        CaseInsensitive = caseInsensitive;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
        Crlf = crlf;
        LineTerminator = lineTerminator;
        Minimum = minimum;
        Maximum = maximum;
        Lazy = lazy;
    }

    public RegexSyntaxKind Kind { get; }

    public byte[] Value { get; }

    public bool CaseInsensitive { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public bool Crlf { get; }

    public byte LineTerminator { get; }

    public int Minimum { get; }

    public int? Maximum { get; }

    public bool Lazy { get; }

    public bool AtomMatches(byte value)
    {
        return RegexByteClass.AtomMatches(
            value,
            Kind,
            Value,
            CaseInsensitive,
            MultiLine,
            DotMatchesNewline,
            Crlf,
            LineTerminator);
    }
}
