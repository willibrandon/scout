namespace Scout;

internal readonly struct PatternSetRequiredLiteralGuardElement
{
    private PatternSetRequiredLiteralGuardElement(
        PatternSetRequiredLiteralGuardElementKind kind,
        RegexSimpleSequenceSegment segment,
        RegexSyntaxKind predicateKind,
        bool multiLine,
        bool crlf,
        byte lineTerminator)
    {
        Kind = kind;
        Segment = segment;
        PredicateKind = predicateKind;
        MultiLine = multiLine;
        Crlf = crlf;
        LineTerminator = lineTerminator;
    }

    public PatternSetRequiredLiteralGuardElementKind Kind { get; }

    public RegexSimpleSequenceSegment Segment { get; }

    public RegexSyntaxKind PredicateKind { get; }

    public bool MultiLine { get; }

    public bool Crlf { get; }

    public byte LineTerminator { get; }

    public static PatternSetRequiredLiteralGuardElement CreateByte(RegexSimpleSequenceSegment segment)
    {
        return new PatternSetRequiredLiteralGuardElement(
            PatternSetRequiredLiteralGuardElementKind.Byte,
            segment,
            default,
            multiLine: false,
            crlf: false,
            lineTerminator: 0);
    }

    public static PatternSetRequiredLiteralGuardElement CreatePredicate(
        RegexSyntaxKind kind,
        bool multiLine,
        bool crlf,
        byte lineTerminator)
    {
        return new PatternSetRequiredLiteralGuardElement(
            PatternSetRequiredLiteralGuardElementKind.Predicate,
            default,
            kind,
            multiLine,
            crlf,
            lineTerminator);
    }

    public bool PredicateMatches(ReadOnlySpan<byte> haystack, int position)
    {
        return RegexByteClass.PredicateMatches(
            haystack,
            position,
            PredicateKind,
            MultiLine,
            Crlf,
            LineTerminator,
            utf8: false,
            unicodeClasses: false);
    }
}
