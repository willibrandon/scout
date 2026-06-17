namespace Scout;

internal readonly struct RegexFixedWidthAtomCacheKey : IEquatable<RegexFixedWidthAtomCacheKey>
{
    private readonly RegexSyntaxKind kind;
    private readonly ReadOnlyMemory<byte> value;
    private readonly int valueHash;
    private readonly int options;

    public RegexFixedWidthAtomCacheKey(RegexAtomNode atom, RegexCompileOptions options)
    {
        kind = atom.Kind;
        value = atom.Value;
        valueHash = HashValue(atom.Value.Span);
        this.options = PackOptions(options);
    }

    public bool Equals(RegexFixedWidthAtomCacheKey other)
    {
        return kind == other.kind &&
            options == other.options &&
            value.Span.SequenceEqual(other.value.Span);
    }

    public override bool Equals(object? obj)
    {
        return obj is RegexFixedWidthAtomCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(kind, valueHash, options);
    }

    private static int HashValue(ReadOnlySpan<byte> value)
    {
        var hash = new HashCode();
        for (int index = 0; index < value.Length; index++)
        {
            hash.Add(value[index]);
        }

        return hash.ToHashCode();
    }

    private static int PackOptions(RegexCompileOptions options)
    {
        int packed = options.LineTerminator;
        packed |= options.CaseInsensitive ? 1 << 8 : 0;
        packed |= options.MultiLine ? 1 << 9 : 0;
        packed |= options.DotMatchesNewline ? 1 << 10 : 0;
        packed |= options.Crlf ? 1 << 11 : 0;
        packed |= options.Utf8 ? 1 << 12 : 0;
        packed |= options.UnicodeClasses ? 1 << 13 : 0;
        return packed;
    }
}
