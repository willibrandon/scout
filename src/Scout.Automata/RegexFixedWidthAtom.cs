namespace Scout;

internal readonly struct RegexFixedWidthAtom
{
    private readonly byte literal;
    private readonly bool[]? lookup;

    private RegexFixedWidthAtom(byte literal, bool[]? lookup)
    {
        this.literal = literal;
        this.lookup = lookup;
    }

    public static RegexFixedWidthAtom CreateLiteral(byte literal)
    {
        return new RegexFixedWidthAtom(literal, null);
    }

    public static RegexFixedWidthAtom CreateLookup(RegexAtomNode atom, RegexCompileOptions options)
    {
        bool[] lookup = new bool[256];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            lookup[value] = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
        }

        return new RegexFixedWidthAtom(0, lookup);
    }

    public bool Matches(byte value)
    {
        return lookup is null ? value == literal : lookup[value];
    }

    public bool TryGetLiteral(out byte value)
    {
        value = literal;
        return lookup is null;
    }

    public void CopyMatchingBytes(Span<byte> destination, out int count)
    {
        if (lookup is null)
        {
            destination[0] = literal;
            count = 1;
            return;
        }

        int write = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (lookup[value])
            {
                destination[write++] = (byte)value;
            }
        }

        count = write;
    }
}
