using System.Runtime.CompilerServices;

namespace Scout;

internal readonly struct RegexFixedWidthAtomCacheKey : IEquatable<RegexFixedWidthAtomCacheKey>
{
    private readonly RegexAtomNode atom;
    private readonly int options;

    public RegexFixedWidthAtomCacheKey(RegexAtomNode atom, RegexCompileOptions options)
    {
        this.atom = atom;
        this.options = PackOptions(options);
    }

    public bool Equals(RegexFixedWidthAtomCacheKey other)
    {
        return ReferenceEquals(atom, other.atom) && options == other.options;
    }

    public override bool Equals(object? obj)
    {
        return obj is RegexFixedWidthAtomCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RuntimeHelpers.GetHashCode(atom), options);
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
