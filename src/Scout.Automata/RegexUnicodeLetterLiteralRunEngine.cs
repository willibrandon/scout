using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexUnicodeLetterLiteralRunEngine
{
    private const int MinimumLiteralLength = 2;

    private readonly byte[][] literals;
    private readonly RegexCaseSensitiveLiteralSetScanner? scanner;
    private readonly MemmemFinder? singleLiteralFinder;

    private RegexUnicodeLetterLiteralRunEngine(byte[][] literals, RegexCaseSensitiveLiteralSetScanner? scanner)
    {
        this.literals = literals;
        this.scanner = scanner;
        singleLiteralFinder = literals.Length == 1 ? new MemmemFinder(literals[0]) : null;
    }

    public static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexUnicodeLetterLiteralRunEngine? engine)
    {
        engine = null;
        if (!options.UnicodeClasses || options.CaseInsensitive)
        {
            return false;
        }

        root = UnwrapTransparentGroups(root);
        if (root is not RegexAlternationNode alternation || alternation.Alternatives.Count == 0)
        {
            return false;
        }

        var literals = new List<byte[]>();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryGetLetterRunLiteral(alternation.Alternatives[index], out byte[] literal) ||
                !AddDistinctLiteral(literals, literal))
            {
                return false;
            }
        }

        if (literals.Count == 0)
        {
            return false;
        }

        RegexCaseSensitiveLiteralSetScanner.TryCreate(literals, out RegexCaseSensitiveLiteralSetScanner? scanner);
        if (literals.Count > 1 && scanner is null)
        {
            return false;
        }

        engine = new RegexUnicodeLetterLiteralRunEngine(literals.ToArray(), scanner);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int lowerBound = Math.Clamp(startAt, 0, haystack.Length);
        int searchAt = lowerBound;
        while (TryFindLiteral(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            if (TryBuildMatch(haystack, candidate.Match.Start, literals[candidate.LiteralId].Length, lowerBound, out RegexMatch match))
            {
                return match;
            }

            searchAt = candidate.Match.Start + 1;
        }

        return null;
    }

    public RegexMatch? MatchAt(ReadOnlySpan<byte> haystack, int startAt)
    {
        int start = Math.Clamp(startAt, 0, haystack.Length);
        return TryMatchAt(haystack, start, out int length)
            ? new RegexMatch(start, length)
            : null;
    }

    public long CountMatches(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: false);
    }

    public long SumMatchSpans(ReadOnlySpan<byte> haystack, int startAt)
    {
        return CountOrSum(haystack, startAt, sumSpans: true);
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if ((uint)start >= (uint)haystack.Length ||
            !TryUnicodeLetterMatchLength(haystack, start, out int scalarLength))
        {
            return false;
        }

        int runEnd = start + scalarLength;
        while (TryUnicodeLetterMatchLength(haystack, runEnd, out scalarLength))
        {
            runEnd += scalarLength;
        }

        int searchAt = start + 1;
        while (TryFindLiteral(haystack, searchAt, out RegexLiteralSetCandidate candidate))
        {
            int literalStart = candidate.Match.Start;
            int literalEnd = literalStart + literals[candidate.LiteralId].Length;
            if (literalStart >= runEnd)
            {
                return false;
            }

            if (literalStart > start && literalEnd < runEnd)
            {
                length = runEnd - start;
                return true;
            }

            searchAt = literalStart + 1;
        }

        return false;
    }

    private long CountOrSum(ReadOnlySpan<byte> haystack, int startAt, bool sumSpans)
    {
        long count = 0;
        long spanSum = 0;
        int offset = Math.Clamp(startAt, 0, haystack.Length);
        while (Find(haystack, offset) is RegexMatch match)
        {
            count++;
            if (sumSpans)
            {
                spanSum += match.Length;
            }

            offset = match.End;
        }

        return sumSpans ? spanSum : count;
    }

    private bool TryFindLiteral(ReadOnlySpan<byte> haystack, int startAt, out RegexLiteralSetCandidate candidate)
    {
        if (scanner is not null)
        {
            RegexLiteralSetCandidate? found = scanner.Find(haystack, startAt);
            candidate = found.GetValueOrDefault();
            return found.HasValue;
        }

        int start = Math.Clamp(startAt, 0, haystack.Length);
        int offset = singleLiteralFinder!.Find(haystack[start..]);
        if (offset < 0)
        {
            candidate = default;
            return false;
        }

        candidate = new RegexLiteralSetCandidate(0, new RegexMatch(start + offset, literals[0].Length));
        return true;
    }

    private static bool TryBuildMatch(
        ReadOnlySpan<byte> haystack,
        int literalStart,
        int literalLength,
        int lowerBound,
        out RegexMatch match)
    {
        match = default;
        if (!TryPreviousUnicodeLetterStart(haystack, literalStart, out int previousStart) ||
            !TryUnicodeLetterMatchLength(haystack, literalStart + literalLength, out _))
        {
            return false;
        }

        int runStart = previousStart;
        while (TryPreviousUnicodeLetterStart(haystack, runStart, out previousStart))
        {
            runStart = previousStart;
        }

        int matchStart = runStart;
        if (matchStart < lowerBound)
        {
            matchStart = FirstUtf8BoundaryAtOrAfter(haystack, lowerBound, literalStart);
            if (matchStart >= literalStart)
            {
                return false;
            }
        }

        int runEnd = literalStart + literalLength;
        while (TryUnicodeLetterMatchLength(haystack, runEnd, out int scalarLength))
        {
            runEnd += scalarLength;
        }

        match = new RegexMatch(matchStart, runEnd - matchStart);
        return true;
    }

    private static int FirstUtf8BoundaryAtOrAfter(ReadOnlySpan<byte> haystack, int position, int limit)
    {
        while (position < limit && !RegexByteClass.IsUtf8Boundary(haystack, position))
        {
            position++;
        }

        return position;
    }

    private static bool TryPreviousUnicodeLetterStart(ReadOnlySpan<byte> haystack, int position, out int start)
    {
        start = 0;
        if (position <= 0)
        {
            return false;
        }

        byte previous = haystack[position - 1];
        if (previous <= 0x7F)
        {
            if (!RegexSimpleSequenceSegment.IsAsciiLetter(previous))
            {
                return false;
            }

            start = position - 1;
            return true;
        }

        int minimum = Math.Max(0, position - 4);
        for (int candidate = position - 2; candidate >= minimum; candidate--)
        {
            if (!RegexByteClass.IsUtf8Boundary(haystack, candidate))
            {
                continue;
            }

            if (Rune.DecodeFromUtf8(haystack[candidate..position], out Rune rune, out int length) == OperationStatus.Done &&
                candidate + length == position &&
                RegexUnicodeTables.IsGeneralCategory(RegexUnicodePropertyKind.Letter, rune))
            {
                start = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryUnicodeLetterMatchLength(ReadOnlySpan<byte> haystack, int position, out int scalarLength)
    {
        scalarLength = 0;
        if (position >= haystack.Length)
        {
            return false;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            scalarLength = RegexSimpleSequenceSegment.IsAsciiLetter(first) ? 1 : 0;
            return scalarLength != 0;
        }

        if (!RegexByteClass.IsUtf8Boundary(haystack, position) ||
            Rune.DecodeFromUtf8(haystack[position..], out Rune rune, out scalarLength) != OperationStatus.Done ||
            !RegexUnicodeTables.IsGeneralCategory(RegexUnicodePropertyKind.Letter, rune))
        {
            scalarLength = 0;
            return false;
        }

        return true;
    }

    private static bool TryGetLetterRunLiteral(RegexSyntaxNode node, out byte[] literal)
    {
        literal = [];
        node = UnwrapTransparentGroups(node);
        if (node is not RegexSequenceNode { Nodes.Count: >= 3 } sequence ||
            !IsOneOrMoreUnicodeLetter(sequence.Nodes[0]) ||
            !IsOneOrMoreUnicodeLetter(sequence.Nodes[^1]))
        {
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 1; index < sequence.Nodes.Count - 1; index++)
        {
            if (UnwrapTransparentGroups(sequence.Nodes[index]) is not RegexAtomNode
                {
                    Kind: RegexSyntaxKind.Literal,
                } literalAtom ||
                !IsAsciiLetterLiteral(literalAtom.Value.Span))
            {
                return false;
            }

            bytes.AddRange(literalAtom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return literal.Length >= MinimumLiteralLength;
    }

    private static bool IsOneOrMoreUnicodeLetter(RegexSyntaxNode node)
    {
        return UnwrapTransparentGroups(node) is RegexRepetitionNode
        {
            Minimum: 1,
            Maximum: null,
            Lazy: false,
            Child: RegexAtomNode atom,
        } && IsUnicodeLetterAtom(atom);
    }

    private static bool IsUnicodeLetterAtom(RegexAtomNode atom)
    {
        return atom.Kind == RegexSyntaxKind.LetterClass ||
            atom is
            {
                Kind: RegexSyntaxKind.UnicodePropertyClass,
                Value.Length: 1,
            } &&
            atom.Value.Span[0] == (byte)RegexUnicodePropertyKind.Letter;
    }

    private static bool IsAsciiLetterLiteral(ReadOnlySpan<byte> literal)
    {
        for (int index = 0; index < literal.Length; index++)
        {
            if (!RegexSimpleSequenceSegment.IsAsciiLetter(literal[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AddDistinctLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return true;
            }
        }

        literals.Add(literal);
        return true;
    }

    private static RegexSyntaxNode UnwrapTransparentGroups(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode group &&
            string.IsNullOrEmpty(group.EnabledFlags) &&
            string.IsNullOrEmpty(group.DisabledFlags))
        {
            node = group.Child;
        }

        return node;
    }
}
