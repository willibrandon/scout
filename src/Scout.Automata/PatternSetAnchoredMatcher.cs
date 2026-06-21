namespace Scout;

internal sealed class PatternSetAnchoredMatcher
{
    private const int AutomatonKind = 0;
    private const int LiteralKind = 1;
    private const int LiteralAlternationKind = 2;
    private const int BoundaryLiteralKind = 3;
    private const int RepeatedByteSetKind = 4;
    private const int LeadingByteSetRunKind = 5;
    private const int SeparatedRunKind = 6;
    private const int DotKind = 7;

    private readonly int kind;
    private RegexAutomaton? automaton;
    private byte[]? literal;
    private byte[][]? alternatives;
    private bool asciiCaseInsensitive;
    private bool[]? firstAllowed;
    private bool[]? restAllowed;
    private int minimum;
    private byte separator;
    private bool multiLine;
    private bool dotMatchesNewline;
    private bool crlf;
    private byte lineTerminator;
    private bool utf8;
    private bool unicodeClasses;

    private PatternSetAnchoredMatcher(int patternId, int kind)
    {
        PatternId = patternId;
        this.kind = kind;
    }

    public int PatternId { get; }

    public static PatternSetAnchoredMatcher Create(
        int patternId,
        byte[] pattern,
        PatternSetPatternPlan plan,
        RegexCompileOptions options,
        RegexAutomaton? automaton)
    {
        if (TryCreateSpecialized(patternId, pattern, plan, options, out PatternSetAnchoredMatcher? matcher))
        {
            return matcher!;
        }

        if (automaton is not null)
        {
            return new PatternSetAnchoredMatcher(patternId, AutomatonKind)
            {
                automaton = automaton,
            };
        }

        if (plan.LiteralPatterns is not null)
        {
            return CreateLiteralAlternation(patternId, plan.LiteralPatterns, options.CaseInsensitive);
        }

        if (plan.BoundaryLiteralPatterns is not null)
        {
            return CreateBoundaryLiteral(patternId, plan.BoundaryLiteralPatterns[0], options);
        }

        return CreateLiteral(patternId, pattern, options.CaseInsensitive);
    }

    public int MatchAt(ReadOnlySpan<byte> haystack, int start)
    {
        return kind switch
        {
            AutomatonKind => MatchAutomaton(haystack, start),
            LiteralKind => MatchLiteral(haystack, start, literal!),
            LiteralAlternationKind => MatchLiteralAlternation(haystack, start),
            BoundaryLiteralKind => MatchBoundaryLiteral(haystack, start),
            RepeatedByteSetKind => MatchRepeatedByteSet(haystack, start),
            LeadingByteSetRunKind => MatchLeadingByteSetRun(haystack, start),
            SeparatedRunKind => MatchSeparatedRun(haystack, start),
            DotKind => MatchDot(haystack, start),
            _ => -1,
        };
    }

    public void AddStartBytes(bool[] bytes)
    {
        switch (kind)
        {
            case AutomatonKind:
                if (automaton!.TryAddStartBytes(bytes))
                {
                    return;
                }

                Array.Fill(bytes, true);
                return;
            case LiteralKind:
                AddLiteralStartBytes(bytes, literal!);
                return;
            case LiteralAlternationKind:
                for (int index = 0; index < alternatives!.Length; index++)
                {
                    AddLiteralStartBytes(bytes, alternatives[index]);
                }

                return;
            case BoundaryLiteralKind:
                AddLiteralStartBytes(bytes, literal!);
                return;
            case RepeatedByteSetKind:
            case LeadingByteSetRunKind:
            case SeparatedRunKind:
                AddAllowedStartBytes(bytes, firstAllowed!);
                return;
            case DotKind:
                AddDotStartBytes(bytes);
                return;
        }
    }

    private static bool TryCreateSpecialized(
        int patternId,
        byte[] pattern,
        PatternSetPatternPlan plan,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (options.CaseInsensitive ||
            options.Utf8)
        {
            return false;
        }

        if (plan.BoundaryLiteralPatterns is not null)
        {
            matcher = CreateBoundaryLiteral(patternId, plan.BoundaryLiteralPatterns[0], options);
            return true;
        }

        if (plan.LiteralPatterns is not null)
        {
            matcher = CreateLiteralAlternation(patternId, plan.LiteralPatterns, asciiCaseInsensitive: false);
            return true;
        }

        RegexSyntaxNode? root = plan.Tree?.Root;
        if (root is null)
        {
            if (pattern.Length == 0)
            {
                return false;
            }

            matcher = CreateLiteral(patternId, pattern, asciiCaseInsensitive: false);
            return true;
        }

        RegexCompileOptions effectiveOptions = options;
        root = UnwrapGroupsAndSingleSequences(root, ref effectiveOptions);
        return TryCreateLiteralAlternationMatcher(patternId, root, effectiveOptions, out matcher) ||
            TryCreateRepeatedByteSetMatcher(patternId, root, effectiveOptions, out matcher) ||
            TryCreateSeparatedRunMatcher(patternId, root, effectiveOptions, out matcher) ||
            TryCreateLeadingByteSetRunMatcher(patternId, root, effectiveOptions, out matcher) ||
            TryCreateDotMatcher(patternId, root, effectiveOptions, out matcher);
    }

    private static PatternSetAnchoredMatcher CreateLiteral(int patternId, byte[] literal, bool asciiCaseInsensitive)
    {
        return new PatternSetAnchoredMatcher(patternId, LiteralKind)
        {
            literal = literal,
            asciiCaseInsensitive = asciiCaseInsensitive,
        };
    }

    private static PatternSetAnchoredMatcher CreateLiteralAlternation(
        int patternId,
        IReadOnlyList<byte[]> alternatives,
        bool asciiCaseInsensitive)
    {
        byte[][] copied = new byte[alternatives.Count][];
        for (int index = 0; index < alternatives.Count; index++)
        {
            copied[index] = alternatives[index].ToArray();
        }

        return new PatternSetAnchoredMatcher(patternId, LiteralAlternationKind)
        {
            alternatives = copied,
            asciiCaseInsensitive = asciiCaseInsensitive,
        };
    }

    private static PatternSetAnchoredMatcher CreateBoundaryLiteral(int patternId, byte[] literal, RegexCompileOptions options)
    {
        return new PatternSetAnchoredMatcher(patternId, BoundaryLiteralKind)
        {
            literal = literal,
            multiLine = options.MultiLine,
            crlf = options.Crlf,
            lineTerminator = options.LineTerminator,
            utf8 = options.Utf8,
            unicodeClasses = options.UnicodeClasses,
        };
    }

    private static bool TryCreateDotMatcher(
        int patternId,
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (options.Utf8 ||
            options.UnicodeClasses ||
            node is not RegexAtomNode { Kind: RegexSyntaxKind.Dot })
        {
            return false;
        }

        matcher = new PatternSetAnchoredMatcher(patternId, DotKind)
        {
            dotMatchesNewline = options.DotMatchesNewline,
            crlf = options.Crlf,
            lineTerminator = options.LineTerminator,
        };
        return true;
    }

    private static bool TryCreateRepeatedByteSetMatcher(
        int patternId,
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (node is not RegexRepetitionNode { Minimum: > 0, Maximum: null } repetition ||
            !TryCreateByteSet(UnwrapGroupsAndSingleSequences(repetition.Child, ref options), options, out bool[] allowed))
        {
            return false;
        }

        matcher = new PatternSetAnchoredMatcher(patternId, RepeatedByteSetKind)
        {
            firstAllowed = allowed,
            minimum = repetition.Minimum,
        };
        return true;
    }

    private static bool TryCreateLeadingByteSetRunMatcher(
        int patternId,
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (node is not RegexSequenceNode { Nodes.Count: 2 } sequence)
        {
            return false;
        }

        RegexCompileOptions firstOptions = options;
        RegexSyntaxNode first = UnwrapGroupsAndSingleSequences(sequence.Nodes[0], ref firstOptions);
        if (!TryCreateByteSet(first, firstOptions, out bool[] firstAllowed))
        {
            return false;
        }

        RegexCompileOptions restOptions = options;
        RegexSyntaxNode rest = UnwrapGroupsAndSingleSequences(sequence.Nodes[1], ref restOptions);
        if (rest is not RegexRepetitionNode { Minimum: 0, Maximum: null } repetition ||
            !TryCreateByteSet(UnwrapGroupsAndSingleSequences(repetition.Child, ref restOptions), restOptions, out bool[] restAllowed))
        {
            return false;
        }

        matcher = new PatternSetAnchoredMatcher(patternId, LeadingByteSetRunKind)
        {
            firstAllowed = firstAllowed,
            restAllowed = restAllowed,
        };
        return true;
    }

    private static bool TryCreateSeparatedRunMatcher(
        int patternId,
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (node is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetUnboundedByteSetRepetition(sequence.Nodes[0], options, minimum: 1, out bool[] firstAllowed) ||
            !TryGetSeparatedRunTail(sequence.Nodes[1], options, out byte separator, out bool[] restAllowed))
        {
            return false;
        }

        matcher = new PatternSetAnchoredMatcher(patternId, SeparatedRunKind)
        {
            firstAllowed = firstAllowed,
            separator = separator,
            restAllowed = restAllowed,
        };
        return true;
    }

    private static bool TryGetSeparatedRunTail(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out byte separator,
        out bool[] restAllowed)
    {
        separator = 0;
        restAllowed = [];
        RegexCompileOptions tailOptions = options;
        node = UnwrapGroupsAndSingleSequences(node, ref tailOptions);
        if (node is not RegexRepetitionNode { Minimum: 0, Maximum: null } outer)
        {
            return false;
        }

        RegexCompileOptions childOptions = tailOptions;
        RegexSyntaxNode child = UnwrapGroupsAndSingleSequences(outer.Child, ref childOptions);
        if (child is not RegexSequenceNode { Nodes.Count: 2 } sequence ||
            !TryGetSingleByteLiteral(sequence.Nodes[0], childOptions, out separator) ||
            !TryGetUnboundedByteSetRepetition(sequence.Nodes[1], childOptions, minimum: 1, out restAllowed))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetUnboundedByteSetRepetition(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        int minimum,
        out bool[] allowed)
    {
        allowed = [];
        RegexCompileOptions effectiveOptions = options;
        node = UnwrapGroupsAndSingleSequences(node, ref effectiveOptions);
        return node is RegexRepetitionNode { Maximum: null } repetition &&
            repetition.Minimum == minimum &&
            TryCreateByteSet(
                UnwrapGroupsAndSingleSequences(repetition.Child, ref effectiveOptions),
                effectiveOptions,
                out allowed);
    }

    private static bool TryCreateLiteralAlternationMatcher(
        int patternId,
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out PatternSetAnchoredMatcher? matcher)
    {
        matcher = null;
        if (TryGetLiteral(node, options, out byte[] literal))
        {
            matcher = CreateLiteral(patternId, literal, asciiCaseInsensitive: false);
            return true;
        }

        if (node is not RegexAlternationNode alternation)
        {
            return false;
        }

        var literals = new List<byte[]>();
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            RegexCompileOptions alternativeOptions = options;
            RegexSyntaxNode alternative = UnwrapGroupsAndSingleSequences(alternation.Alternatives[index], ref alternativeOptions);
            if (!TryGetLiteral(alternative, alternativeOptions, out byte[] alternativeLiteral) ||
                alternativeLiteral.Length == 0)
            {
                return false;
            }

            literals.Add(alternativeLiteral);
        }

        matcher = CreateLiteralAlternation(patternId, literals, asciiCaseInsensitive: false);
        return true;
    }

    private static bool TryGetLiteral(RegexSyntaxNode node, RegexCompileOptions options, out byte[] literal)
    {
        var bytes = new List<byte>();
        if (TryAppendLiteral(node, options, bytes))
        {
            literal = bytes.ToArray();
            return true;
        }

        literal = [];
        return false;
    }

    private static bool TryAppendLiteral(RegexSyntaxNode node, RegexCompileOptions options, List<byte> bytes)
    {
        RegexCompileOptions effectiveOptions = options;
        node = UnwrapGroupsAndSingleSequences(node, ref effectiveOptions);
        switch (node)
        {
            case RegexEmptyNode:
                return true;
            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                bytes.AddRange(atom.Value.Span);
                return true;
            case RegexSequenceNode sequence:
                for (int index = 0; index < sequence.Nodes.Count; index++)
                {
                    if (!TryAppendLiteral(sequence.Nodes[index], effectiveOptions, bytes))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static bool TryGetSingleByteLiteral(RegexSyntaxNode node, RegexCompileOptions options, out byte value)
    {
        value = 0;
        if (!TryGetLiteral(node, options, out byte[] literal) ||
            literal.Length != 1)
        {
            return false;
        }

        value = literal[0];
        return true;
    }

    private static bool TryCreateByteSet(RegexSyntaxNode node, RegexCompileOptions options, out bool[] allowed)
    {
        allowed = [];
        node = UnwrapGroupsAndSingleSequences(node, ref options);
        if (node is not RegexAtomNode atom ||
            atom.Kind is not (RegexSyntaxKind.Literal
                or RegexSyntaxKind.Dot
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.CharacterClass
                or RegexSyntaxKind.ByteClass
                or RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.NotWhitespaceClass) ||
            atom.Kind == RegexSyntaxKind.Literal && atom.Value.Length != 1 ||
            RegexByteClass.RequiresUtf8ScalarMatch(
                atom.Kind,
                atom.Value.Span,
                options.Utf8,
                options.CaseInsensitive,
                options.UnicodeClasses))
        {
            return false;
        }

        allowed = new bool[256];
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            allowed[value] = RegexByteClass.AtomMatches(
                (byte)value,
                atom.Kind,
                atom.Value.Span,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator);
        }

        return true;
    }

    private static RegexSyntaxNode UnwrapGroupsAndSingleSequences(RegexSyntaxNode node, ref RegexCompileOptions options)
    {
        while (true)
        {
            switch (node)
            {
                case RegexSequenceNode { Nodes.Count: 1 } sequence:
                    node = sequence.Nodes[0];
                    continue;
                case RegexGroupNode group:
                    options = options.Apply(group.EnabledFlags, group.DisabledFlags);
                    node = group.Child;
                    continue;
                default:
                    return node;
            }
        }
    }

    private int MatchAutomaton(ReadOnlySpan<byte> haystack, int start)
    {
        RegexMatch? match = automaton!.MatchAt(haystack, start);
        return match.HasValue ? match.Value.Length : -1;
    }

    private int MatchLiteralAlternation(ReadOnlySpan<byte> haystack, int start)
    {
        for (int index = 0; index < alternatives!.Length; index++)
        {
            int length = MatchLiteral(haystack, start, alternatives[index]);
            if (length >= 0)
            {
                return length;
            }
        }

        return -1;
    }

    private int MatchLiteral(ReadOnlySpan<byte> haystack, int start, byte[] candidate)
    {
        if (candidate.Length > haystack.Length - start)
        {
            return -1;
        }

        for (int index = 0; index < candidate.Length; index++)
        {
            if (!ByteEquals(haystack[start + index], candidate[index], asciiCaseInsensitive))
            {
                return -1;
            }
        }

        return candidate.Length;
    }

    private int MatchBoundaryLiteral(ReadOnlySpan<byte> haystack, int start)
    {
        byte[] candidate = literal!;
        if (candidate.Length > haystack.Length - start ||
            !HasWordBoundaryBeforeAsciiLiteral(haystack, start))
        {
            return -1;
        }

        for (int index = 0; index < candidate.Length; index++)
        {
            if (haystack[start + index] != candidate[index])
            {
                return -1;
            }
        }

        int end = start + candidate.Length;
        return HasWordBoundaryAfterAsciiLiteral(haystack, end) ? candidate.Length : -1;
    }

    private bool HasWordBoundaryBeforeAsciiLiteral(ReadOnlySpan<byte> haystack, int start)
    {
        if (start > 0 &&
            haystack[start - 1] <= 0x7F)
        {
            return !IsAsciiWord(haystack[start - 1]);
        }

        return RegexByteClass.PredicateMatches(
            haystack,
            start,
            RegexSyntaxKind.WordBoundary,
            multiLine,
            crlf,
            lineTerminator,
            utf8,
            unicodeClasses);
    }

    private bool HasWordBoundaryAfterAsciiLiteral(ReadOnlySpan<byte> haystack, int end)
    {
        if (end < haystack.Length &&
            haystack[end] <= 0x7F)
        {
            return !IsAsciiWord(haystack[end]);
        }

        return RegexByteClass.PredicateMatches(
            haystack,
            end,
            RegexSyntaxKind.WordBoundary,
            multiLine,
            crlf,
            lineTerminator,
            utf8,
            unicodeClasses);
    }

    private int MatchRepeatedByteSet(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = start;
        bool[] allowed = firstAllowed!;
        while (offset < haystack.Length && allowed[haystack[offset]])
        {
            offset++;
        }

        int length = offset - start;
        return length >= minimum ? length : -1;
    }

    private int MatchLeadingByteSetRun(ReadOnlySpan<byte> haystack, int start)
    {
        if (start >= haystack.Length || !firstAllowed![haystack[start]])
        {
            return -1;
        }

        int offset = start + 1;
        bool[] allowed = restAllowed!;
        while (offset < haystack.Length && allowed[haystack[offset]])
        {
            offset++;
        }

        return offset - start;
    }

    private int MatchSeparatedRun(ReadOnlySpan<byte> haystack, int start)
    {
        int offset = ConsumeRun(haystack, start, firstAllowed!);
        if (offset == start)
        {
            return -1;
        }

        bool[] allowed = restAllowed!;
        while (offset + 1 < haystack.Length &&
            haystack[offset] == separator &&
            allowed[haystack[offset + 1]])
        {
            offset = ConsumeRun(haystack, offset + 1, allowed);
        }

        return offset - start;
    }

    private int MatchDot(ReadOnlySpan<byte> haystack, int start)
    {
        if (start >= haystack.Length)
        {
            return -1;
        }

        byte value = haystack[start];
        if (dotMatchesNewline ||
            (crlf ? value is not ((byte)'\n' or (byte)'\r') : value != lineTerminator))
        {
            return 1;
        }

        return -1;
    }

    private void AddLiteralStartBytes(bool[] bytes, byte[] candidate)
    {
        if (candidate.Length == 0)
        {
            Array.Fill(bytes, true);
            return;
        }

        bytes[candidate[0]] = true;
        if (asciiCaseInsensitive)
        {
            byte folded = FoldAscii(candidate[0]);
            if (folded is >= (byte)'a' and <= (byte)'z')
            {
                bytes[folded] = true;
                bytes[folded - 0x20] = true;
            }
        }
    }

    private void AddDotStartBytes(bool[] bytes)
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            byte current = (byte)value;
            if (dotMatchesNewline ||
                (crlf ? current is not ((byte)'\n' or (byte)'\r') : current != lineTerminator))
            {
                bytes[value] = true;
            }
        }
    }

    private static void AddAllowedStartBytes(bool[] bytes, bool[] allowed)
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (allowed[value])
            {
                bytes[value] = true;
            }
        }
    }

    private static int ConsumeRun(ReadOnlySpan<byte> haystack, int start, bool[] allowed)
    {
        int offset = start;
        while (offset < haystack.Length && allowed[haystack[offset]])
        {
            offset++;
        }

        return offset;
    }

    private static bool IsAsciiWord(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_';
    }

    private static bool ByteEquals(byte left, byte right, bool asciiCaseInsensitive)
    {
        return asciiCaseInsensitive
            ? FoldAscii(left) == FoldAscii(right)
            : left == right;
    }

    private static byte FoldAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }
}
