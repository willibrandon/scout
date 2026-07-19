using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Scout;

/// <summary>
/// Maintains the mutable state used while parsing a regex syntax tree.
/// </summary>
internal sealed class RegexSyntaxParseState(ReadOnlyMemory<byte> pattern)
{
    private readonly HashSet<string> _captureNames = new(StringComparer.Ordinal);
    private readonly ReadOnlyMemory<byte> _pattern = pattern;
    private int _captureCount;
    private bool _extendedMode;
    private int _index;

    private ReadOnlySpan<byte> Pattern => _pattern.Span;

    /// <summary>
    /// Parses the configured regex pattern.
    /// </summary>
    /// <returns>The parsed regex syntax tree.</returns>
    public RegexSyntaxTree Parse()
    {
        RegexSyntaxNode root = ParseAlternation(stopAtGroupEnd: false);
        if (_index != Pattern.Length)
        {
            Throw("unexpected trailing token");
        }

        return new RegexSyntaxTree(_pattern, root, _captureCount);
    }

    private RegexSyntaxNode ParseAlternation(bool stopAtGroupEnd)
    {
        int position = _index;
        var alternatives = new List<RegexSyntaxNode>();
        var inheritedFlags = new List<RegexInlineFlagsNode>();
        while (true)
        {
            RegexSyntaxNode alternative = ParseSequence(stopAtGroupEnd);
            alternatives.Add(PrependInheritedFlags(alternative, inheritedFlags));
            CollectUnscopedFlags(alternative, inheritedFlags);
            if (_index >= Pattern.Length || Pattern[_index] != (byte)'|')
            {
                break;
            }

            _index++;
        }

        return alternatives.Count == 1
            ? alternatives[0]
            : new RegexAlternationNode(alternatives, position);
    }

    private static RegexSyntaxNode PrependInheritedFlags(
        RegexSyntaxNode alternative,
        List<RegexInlineFlagsNode> inheritedFlags)
    {
        if (inheritedFlags.Count == 0)
        {
            return alternative;
        }

        var nodes = new List<RegexSyntaxNode>(inheritedFlags.Count + 1);
        nodes.AddRange(inheritedFlags);
        if (alternative is RegexSequenceNode sequence)
        {
            nodes.AddRange(sequence.Nodes);
        }
        else
        {
            nodes.Add(alternative);
        }

        return new RegexSequenceNode(nodes, alternative.Position);
    }

    private static void CollectUnscopedFlags(
        RegexSyntaxNode alternative,
        List<RegexInlineFlagsNode> inheritedFlags)
    {
        if (alternative is RegexInlineFlagsNode flags)
        {
            AddRuntimeFlags(flags, inheritedFlags);
            return;
        }

        if (alternative is not RegexSequenceNode sequence)
        {
            return;
        }

        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            if (sequence.Nodes[index] is RegexInlineFlagsNode inlineFlags)
            {
                AddRuntimeFlags(inlineFlags, inheritedFlags);
            }
        }
    }

    private static void AddRuntimeFlags(
        RegexInlineFlagsNode flags,
        List<RegexInlineFlagsNode> inheritedFlags)
    {
        string enabledFlags = flags.EnabledFlags.Replace("x", string.Empty, StringComparison.Ordinal);
        string disabledFlags = flags.DisabledFlags.Replace("x", string.Empty, StringComparison.Ordinal);
        if (enabledFlags.Length > 0 || disabledFlags.Length > 0)
        {
            inheritedFlags.Add(new RegexInlineFlagsNode(enabledFlags, disabledFlags, flags.Position));
        }
    }

    private RegexSyntaxNode ParseSequence(bool stopAtGroupEnd)
    {
        int position = _index;
        var nodes = new List<RegexSyntaxNode>();
        while (_index < Pattern.Length)
        {
            if (_extendedMode)
            {
                SkipExtendedPatternWhitespace();
                if (_index >= Pattern.Length)
                {
                    break;
                }
            }

            byte token = Pattern[_index];
            if (token == (byte)'|')
            {
                break;
            }

            if (token == (byte)')')
            {
                if (stopAtGroupEnd)
                {
                    break;
                }

                Throw("unopened group");
            }

            RegexSyntaxNode node = ParseAtom();
            while (TryParseRepetition(node, out RegexSyntaxNode repeated))
            {
                node = repeated;
            }

            nodes.Add(node);
        }

        return nodes.Count switch
        {
            0 => new RegexEmptyNode(position),
            1 => nodes[0],
            _ => new RegexSequenceNode(nodes, position),
        };
    }

    private RegexSyntaxNode ParseAtom()
    {
        int position = _index;
        byte token = Pattern[_index++];
        if (token is (byte)'?' or (byte)'*' or (byte)'+' or (byte)'{')
        {
            Throw("repetition operator missing expression");
        }

        return token switch
        {
            (byte)'(' => ParseGroup(position),
            (byte)'[' => ParseClass(position),
            (byte)'.' => new RegexAtomNode(RegexSyntaxKind.Dot, ReadOnlyMemory<byte>.Empty, position),
            (byte)'^' => new RegexAtomNode(RegexSyntaxKind.StartAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'$' => new RegexAtomNode(RegexSyntaxKind.EndAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'\\' => ParseEscape(position),
            _ => ParseLiteral(position, token),
        };
    }

    private RegexAtomNode ParseLiteral(int position, byte token)
    {
        if (token > 0x7F &&
            Rune.DecodeFromUtf8(Pattern[position..], out Rune rune, out int length) == OperationStatus.Done &&
            length > 1)
        {
            _index = position + length;
            return new RegexAtomNode(
                RegexSyntaxKind.Literal,
                Pattern.Slice(position, length).ToArray(),
                position,
                rune.Value);
        }

        return new RegexAtomNode(RegexSyntaxKind.Literal, new[] { token }, position, token);
    }

    private RegexSyntaxNode ParseGroup(int position)
    {
        if (_index < Pattern.Length && Pattern[_index] == (byte)'?')
        {
            _index++;
            if (_index < Pattern.Length && Pattern[_index] == (byte)':')
            {
                _index++;
                return ParseGroupBody(position, capturing: false, captureName: null, enabledFlags: string.Empty, disabledFlags: string.Empty);
            }

            if (TryParseNamedCapturePrefix(out string? captureName))
            {
                return ParseGroupBody(position, capturing: true, captureName, enabledFlags: string.Empty, disabledFlags: string.Empty);
            }

            ParseInlineFlags(out string enabledFlags, out string disabledFlags, out bool scoped);
            if (scoped)
            {
                bool previousExtendedMode = _extendedMode;
                ApplyParseFlags(enabledFlags, disabledFlags);
                try
                {
                    return ParseGroupBody(position, capturing: false, captureName: null, enabledFlags, disabledFlags);
                }
                finally
                {
                    _extendedMode = previousExtendedMode;
                }
            }

            ApplyParseFlags(enabledFlags, disabledFlags);
            return new RegexInlineFlagsNode(enabledFlags, disabledFlags, position);
        }

        return ParseGroupBody(position, capturing: true, captureName: null, enabledFlags: string.Empty, disabledFlags: string.Empty);
    }

    private RegexGroupNode ParseGroupBody(
        int position,
        bool capturing,
        string? captureName,
        string enabledFlags,
        string disabledFlags)
    {
        bool previousExtendedMode = _extendedMode;
        try
        {
            int captureIndex = 0;
            if (capturing)
            {
                _captureCount++;
                captureIndex = _captureCount;
            }

            RegexSyntaxNode child = ParseAlternation(stopAtGroupEnd: true);
            if (_index >= Pattern.Length || Pattern[_index] != (byte)')')
            {
                Throw("unclosed group");
            }

            _index++;
            return new RegexGroupNode(
                capturing ? RegexSyntaxKind.CapturingGroup : RegexSyntaxKind.NonCapturingGroup,
                child,
                captureIndex,
                captureName,
                enabledFlags,
                disabledFlags,
                position);
        }
        finally
        {
            _extendedMode = previousExtendedMode;
        }
    }

    private void ApplyParseFlags(string enabledFlags, string disabledFlags)
    {
        for (int flagIndex = 0; flagIndex < enabledFlags.Length; flagIndex++)
        {
            if (enabledFlags[flagIndex] == 'x')
            {
                _extendedMode = true;
            }
        }

        for (int flagIndex = 0; flagIndex < disabledFlags.Length; flagIndex++)
        {
            if (disabledFlags[flagIndex] == 'x')
            {
                _extendedMode = false;
            }
        }
    }

    private bool TryParseNamedCapturePrefix(out string? captureName)
    {
        captureName = null;
        int nameStart;
        if (_index + 1 < Pattern.Length && Pattern[_index] == (byte)'P' && Pattern[_index + 1] == (byte)'<')
        {
            nameStart = _index + 2;
        }
        else if (_index < Pattern.Length && Pattern[_index] == (byte)'<')
        {
            nameStart = _index + 1;
        }
        else
        {
            return false;
        }

        int nameEnd = nameStart;
        while (nameEnd < Pattern.Length && IsCaptureNameByte(Pattern[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart ||
            nameStart >= Pattern.Length ||
            !IsCaptureNameStartByte(Pattern[nameStart]) ||
            nameEnd >= Pattern.Length ||
            Pattern[nameEnd] != (byte)'>')
        {
            Throw("invalid capture name");
        }

        captureName = Encoding.ASCII.GetString(Pattern[nameStart..nameEnd]);
        _index = nameEnd + 1;
        if (!_captureNames.Add(captureName))
        {
            Throw("duplicate capture group name");
        }

        return true;
    }

    private void ParseInlineFlags(out string enabledFlags, out string disabledFlags, out bool scoped)
    {
        var enabled = new List<byte>();
        var disabled = new List<byte>();
        bool disabling = false;
        while (_index < Pattern.Length)
        {
            byte token = Pattern[_index];
            if (token == (byte)'-')
            {
                disabling = true;
                _index++;
                continue;
            }

            if (token == (byte)':')
            {
                if (enabled.Count == 0 && disabled.Count == 0)
                {
                    Throw("missing inline flags");
                }

                _index++;
                enabledFlags = Encoding.ASCII.GetString([.. enabled]);
                disabledFlags = Encoding.ASCII.GetString([.. disabled]);
                scoped = true;
                return;
            }

            if (token == (byte)')')
            {
                if (enabled.Count == 0 && disabled.Count == 0)
                {
                    Throw("missing inline flags");
                }

                _index++;
                enabledFlags = Encoding.ASCII.GetString([.. enabled]);
                disabledFlags = Encoding.ASCII.GetString([.. disabled]);
                scoped = false;
                return;
            }

            if (!IsRegexFlagByte(token))
            {
                Throw("unrecognized inline flag");
            }

            if (disabling)
            {
                disabled.Add(token);
            }
            else
            {
                enabled.Add(token);
            }

            _index++;
        }

        Throw("unclosed inline flags");
        enabledFlags = string.Empty;
        disabledFlags = string.Empty;
        scoped = false;
    }

    private RegexAtomNode ParseClass(int position)
    {
        int expressionStart = _index;
        RegexCharacterClass characterClass = ParseBracketedClass(depth: 1);
        int expressionLength = (_index - 1) - expressionStart;
        return new RegexAtomNode(
            RegexSyntaxKind.CharacterClass,
            _pattern.Slice(expressionStart, expressionLength),
            position,
            characterClass: characterClass);
    }

    private RegexCharacterClass ParseBracketedClass(int depth)
    {
        const int nestingLimit = 250;
        if (depth > nestingLimit)
        {
            Throw("character class nesting limit exceeded");
        }

        SkipClassWhitespace();
        if (_index >= Pattern.Length)
        {
            Throw("unclosed character class");
        }

        bool negated = Pattern[_index] == (byte)'^';
        if (negated)
        {
            _index++;
            SkipClassWhitespace();
        }

        var union = new List<RegexClassSetNode>();
        while (_index < Pattern.Length && Pattern[_index] == (byte)'-')
        {
            union.Add(new RegexClassSetNode(RegexClassSetKind.Literal, scalar: '-'));
            _index++;
            SkipClassWhitespace();
        }

        if (union.Count == 0 && _index < Pattern.Length && Pattern[_index] == (byte)']')
        {
            union.Add(new RegexClassSetNode(RegexClassSetKind.Literal, scalar: ']'));
            _index++;
            SkipClassWhitespace();
        }

        RegexClassSetNode? left = null;
        RegexClassSetBinaryOperator? pendingOperator = null;
        while (true)
        {
            SkipClassWhitespace();
            if (_index >= Pattern.Length)
            {
                Throw("unclosed character class");
            }

            if (Pattern[_index] == (byte)']')
            {
                _index++;
                RegexClassSetNode expression = CompleteClassUnion(union);
                if (pendingOperator.HasValue)
                {
                    expression = new RegexClassSetNode(
                        RegexClassSetKind.Binary,
                        left: left,
                        right: expression,
                        binaryOperator: pendingOperator.Value);
                }

                return new RegexCharacterClass(negated, expression);
            }

            if (TryParseClassSetOperator(out RegexClassSetBinaryOperator binaryOperator))
            {
                RegexClassSetNode expression = CompleteClassUnion(union);
                if (pendingOperator.HasValue)
                {
                    expression = new RegexClassSetNode(
                        RegexClassSetKind.Binary,
                        left: left,
                        right: expression,
                        binaryOperator: pendingOperator.Value);
                }

                left = expression;
                pendingOperator = binaryOperator;
                union.Clear();
                continue;
            }

            union.Add(ParseClassSetRangeOrItem(depth));
        }
    }

    private RegexClassSetNode ParseClassSetRangeOrItem(int depth)
    {
        RegexClassSetNode start = ParseClassSetItem(depth);
        SkipClassWhitespace();
        if (_index >= Pattern.Length || Pattern[_index] != (byte)'-')
        {
            return start;
        }

        byte? next = PeekClassSignificantByte(_index + 1);
        if (next is (byte)']' or (byte)'-' || !next.HasValue)
        {
            return start;
        }

        _index++;
        SkipClassWhitespace();
        RegexClassSetNode end = ParseClassSetItem(depth);
        if (start.Kind != RegexClassSetKind.Literal || end.Kind != RegexClassSetKind.Literal)
        {
            Throw("character class range endpoint is not a literal");
        }

        if (start.Scalar > end.Scalar)
        {
            Throw("character class range is invalid");
        }

        return new RegexClassSetNode(
            RegexClassSetKind.Range,
            scalar: start.Scalar,
            rangeEnd: end.Scalar,
            literalKind: start.LiteralKind,
            rangeEndLiteralKind: end.LiteralKind);
    }

    private RegexClassSetNode ParseClassSetItem(int depth)
    {
        if (_index >= Pattern.Length)
        {
            Throw("unclosed character class");
        }

        if (Pattern[_index] == (byte)'\\')
        {
            int escapePosition = _index++;
            RegexSyntaxNode escaped = ParseEscape(escapePosition);
            if (escaped is not RegexAtomNode parsedAtom)
            {
                Throw("invalid escape in character class");
                return new RegexClassSetNode(RegexClassSetKind.Empty);
            }

            RegexAtomNode atom = parsedAtom;

            if (atom.Kind == RegexSyntaxKind.Literal)
            {
                return new RegexClassSetNode(
                    RegexClassSetKind.Literal,
                    scalar: atom.Scalar,
                    literalKind: atom.LiteralKind);
            }

            if (atom.Kind is RegexSyntaxKind.DigitClass
                or RegexSyntaxKind.NotDigitClass
                or RegexSyntaxKind.WordClass
                or RegexSyntaxKind.NotWordClass
                or RegexSyntaxKind.WhitespaceClass
                or RegexSyntaxKind.NotWhitespaceClass
                or RegexSyntaxKind.AnyClass
                or RegexSyntaxKind.UnicodePropertyClass
                or RegexSyntaxKind.NotUnicodePropertyClass)
            {
                return new RegexClassSetNode(
                    RegexClassSetKind.Atom,
                    atomKind: atom.Kind,
                    unicodeProperty: atom.UnicodeProperty);
            }

            Throw("invalid escape in character class");
        }

        if (Pattern[_index] == (byte)'[')
        {
            if (TryParsePosixClassNode(out RegexClassSetNode? posix))
            {
                return posix!;
            }

            if (_index + 1 < Pattern.Length && Pattern[_index + 1] == (byte)':')
            {
                Throw("invalid ASCII character class");
            }

            _index++;
            return new RegexClassSetNode(
                RegexClassSetKind.Bracketed,
                bracketed: ParseBracketedClass(depth + 1));
        }

        int position = _index;
        byte token = Pattern[_index++];
        if (token <= 0x7F)
        {
            return new RegexClassSetNode(RegexClassSetKind.Literal, scalar: token);
        }

        if (Rune.DecodeFromUtf8(Pattern[position..], out Rune rune, out int length) != OperationStatus.Done)
        {
            Throw("invalid UTF-8 scalar in character class");
        }

        _index = position + length;
        return new RegexClassSetNode(RegexClassSetKind.Literal, scalar: rune.Value);
    }

    private bool TryParseClassSetOperator(out RegexClassSetBinaryOperator binaryOperator)
    {
        binaryOperator = RegexClassSetBinaryOperator.Intersection;
        if (_index + 1 >= Pattern.Length || Pattern[_index] != Pattern[_index + 1])
        {
            return false;
        }

        binaryOperator = Pattern[_index] switch
        {
            (byte)'&' => RegexClassSetBinaryOperator.Intersection,
            (byte)'-' => RegexClassSetBinaryOperator.Difference,
            (byte)'~' => RegexClassSetBinaryOperator.SymmetricDifference,
            _ => binaryOperator,
        };
        if (Pattern[_index] != (byte)'&' &&
            Pattern[_index] != (byte)'-' &&
            Pattern[_index] != (byte)'~')
        {
            return false;
        }

        _index += 2;
        return true;
    }

    private bool TryParsePosixClassNode(out RegexClassSetNode? node)
    {
        node = null;
        int start = _index;
        if (_index + 4 >= Pattern.Length ||
            Pattern[_index] != (byte)'[' ||
            Pattern[_index + 1] != (byte)':')
        {
            return false;
        }

        int nameStart = _index + 2;
        bool negated = nameStart < Pattern.Length && Pattern[nameStart] == (byte)'^';
        if (negated)
        {
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd + 1 < Pattern.Length &&
            !(Pattern[nameEnd] == (byte)':' && Pattern[nameEnd + 1] == (byte)']'))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart || nameEnd + 1 >= Pattern.Length ||
            !TryCreatePosixClassNode(
                Pattern[nameStart..nameEnd],
                negated,
                out RegexClassSetNode? expression))
        {
            _index = start;
            return false;
        }

        _index = nameEnd + 2;
        node = expression;
        return true;
    }

    private static bool TryCreatePosixClassNode(
        ReadOnlySpan<byte> name,
        bool negated,
        out RegexClassSetNode? node)
    {
        if (TryGetPosixClassKind(name, out RegexSyntaxKind atomKind))
        {
            node = new RegexClassSetNode(
                RegexClassSetKind.Atom,
                atomKind: atomKind,
                negated: negated);
            return true;
        }

        RegexClassSetNode? expression = null;
        if (name.SequenceEqual("ascii"u8))
        {
            expression = CreateClassRange(0, 0x7F);
        }
        else if (name.SequenceEqual("blank"u8))
        {
            expression = CreateClassUnion(CreateClassLiteral('\t'), CreateClassLiteral(' '));
        }
        else if (name.SequenceEqual("cntrl"u8))
        {
            expression = CreateClassUnion(CreateClassRange(0, 0x1F), CreateClassLiteral(0x7F));
        }
        else if (name.SequenceEqual("graph"u8))
        {
            expression = CreateClassRange('!', '~');
        }
        else if (name.SequenceEqual("lower"u8))
        {
            expression = CreateClassRange('a', 'z');
        }
        else if (name.SequenceEqual("print"u8))
        {
            expression = CreateClassRange(' ', '~');
        }
        else if (name.SequenceEqual("punct"u8))
        {
            expression = CreateClassUnion(
                CreateClassRange('!', '/'),
                CreateClassRange(':', '@'),
                CreateClassRange('[', '`'),
                CreateClassRange('{', '~'));
        }
        else if (name.SequenceEqual("upper"u8))
        {
            expression = CreateClassRange('A', 'Z');
        }
        else if (name.SequenceEqual("xdigit"u8))
        {
            expression = CreateClassUnion(
                CreateClassRange('0', '9'),
                CreateClassRange('A', 'F'),
                CreateClassRange('a', 'f'));
        }

        if (expression is null)
        {
            node = null;
            return false;
        }

        node = new RegexClassSetNode(
            RegexClassSetKind.Bracketed,
            bracketed: new RegexCharacterClass(negated, expression));
        return true;
    }

    private static bool TryGetPosixClassKind(ReadOnlySpan<byte> name, out RegexSyntaxKind atomKind)
    {
        atomKind = RegexSyntaxKind.Empty;
        if (name.SequenceEqual("digit"u8))
        {
            atomKind = RegexSyntaxKind.DigitClass;
        }
        else if (name.SequenceEqual("word"u8))
        {
            atomKind = RegexSyntaxKind.WordClass;
        }
        else if (name.SequenceEqual("space"u8))
        {
            atomKind = RegexSyntaxKind.WhitespaceClass;
        }
        else if (name.SequenceEqual("alpha"u8))
        {
            atomKind = RegexSyntaxKind.LetterClass;
        }
        else if (name.SequenceEqual("alnum"u8))
        {
            atomKind = RegexSyntaxKind.AlphanumericClass;
        }

        return atomKind != RegexSyntaxKind.Empty;
    }

    private static RegexClassSetNode CreateClassLiteral(int scalar)
    {
        return new RegexClassSetNode(RegexClassSetKind.Literal, scalar: scalar);
    }

    private static RegexClassSetNode CreateClassRange(int start, int end)
    {
        return new RegexClassSetNode(RegexClassSetKind.Range, scalar: start, rangeEnd: end);
    }

    private static RegexClassSetNode CreateClassUnion(params RegexClassSetNode[] nodes)
    {
        return new RegexClassSetNode(RegexClassSetKind.Union, items: nodes);
    }

    private static RegexClassSetNode CompleteClassUnion(List<RegexClassSetNode> union)
    {
        return union.Count switch
        {
            0 => new RegexClassSetNode(RegexClassSetKind.Empty),
            1 => union[0],
            _ => new RegexClassSetNode(RegexClassSetKind.Union, items: union.ToArray()),
        };
    }

    private byte? PeekClassSignificantByte(int index)
    {
        while (index < Pattern.Length)
        {
            byte value = Pattern[index];
            if (!_extendedMode)
            {
                return value;
            }

            if (IsExtendedWhitespaceByte(value))
            {
                index++;
                continue;
            }

            if (value == (byte)'#')
            {
                index++;
                while (index < Pattern.Length && Pattern[index] != (byte)'\n')
                {
                    index++;
                }

                continue;
            }

            return value;
        }

        return null;
    }

    private void SkipClassWhitespace()
    {
        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }
    }

    private RegexSyntaxNode ParseEscape(int position)
    {
        if (_index >= Pattern.Length)
        {
            Throw("escape at end of pattern");
        }

        byte escaped = Pattern[_index++];
        if (escaped == (byte)'b' && TryParseNamedWordBoundary(out RegexSyntaxKind boundaryKind))
        {
            return new RegexAtomNode(boundaryKind, ReadOnlyMemory<byte>.Empty, position);
        }

        if (escaped is (byte)'x' or (byte)'u' or (byte)'U')
        {
            return ParseHexEscape(position, escaped);
        }

        if (escaped is (byte)'p' or (byte)'P')
        {
            return ParseUnicodePropertyClass(position, negated: escaped == (byte)'P');
        }

        RegexSyntaxNode? recognized = escaped switch
        {
            (byte)'A' => new RegexAtomNode(RegexSyntaxKind.AbsoluteStartAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'z' => new RegexAtomNode(RegexSyntaxKind.AbsoluteEndAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'b' => new RegexAtomNode(RegexSyntaxKind.WordBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'B' => new RegexAtomNode(RegexSyntaxKind.NotWordBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'<' => new RegexAtomNode(RegexSyntaxKind.WordStartBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'>' => new RegexAtomNode(RegexSyntaxKind.WordEndBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'d' => new RegexAtomNode(RegexSyntaxKind.DigitClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'D' => new RegexAtomNode(RegexSyntaxKind.NotDigitClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'w' => new RegexAtomNode(RegexSyntaxKind.WordClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'W' => new RegexAtomNode(RegexSyntaxKind.NotWordClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'s' => new RegexAtomNode(RegexSyntaxKind.WhitespaceClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'S' => new RegexAtomNode(RegexSyntaxKind.NotWhitespaceClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'a' => CreateEscapedLiteral(position, 0x07, RegexLiteralKind.Special),
            (byte)'f' => CreateEscapedLiteral(position, 0x0C, RegexLiteralKind.Special),
            (byte)'t' => CreateEscapedLiteral(position, '\t', RegexLiteralKind.Special),
            (byte)'n' => CreateEscapedLiteral(position, '\n', RegexLiteralKind.Special),
            (byte)'r' => CreateEscapedLiteral(position, '\r', RegexLiteralKind.Special),
            (byte)'v' => CreateEscapedLiteral(position, 0x0B, RegexLiteralKind.Special),
            _ => null,
        };
        if (recognized is not null)
        {
            return recognized;
        }

        if (IsEscapablePunctuation(escaped))
        {
            RegexLiteralKind literalKind = IsRegexMetaByte(escaped)
                ? RegexLiteralKind.Meta
                : RegexLiteralKind.Superfluous;
            return CreateEscapedLiteral(position, escaped, literalKind);
        }

        if (IsAsciiDigitByte(escaped))
        {
            Throw("backreferences are not supported");
        }

        Throw("unrecognized escape");
        return new RegexEmptyNode(position);
    }

    private RegexAtomNode ParseUnicodePropertyClass(int position, bool negated)
    {
        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }

        ReadOnlySpan<byte> name;
        byte[]? normalizedName = null;
        if (_index < Pattern.Length && Pattern[_index] == (byte)'{')
        {
            int nameStart = _index + 1;
            int nameEnd = nameStart;
            if (_extendedMode)
            {
                _index = nameStart;
                var bytes = new List<byte>();
                while (_index < Pattern.Length && Pattern[_index] != (byte)'}')
                {
                    int beforeWhitespace = _index;
                    SkipExtendedPatternWhitespace();
                    if (_index != beforeWhitespace)
                    {
                        continue;
                    }

                    bytes.Add(Pattern[_index++]);
                }

                nameEnd = _index;
                normalizedName = bytes.ToArray();
            }
            else
            {
                while (nameEnd < Pattern.Length && Pattern[nameEnd] != (byte)'}')
                {
                    nameEnd++;
                }
            }

            if (nameEnd >= Pattern.Length)
            {
                Throw("unclosed Unicode property class");
            }

            if (nameEnd == nameStart || normalizedName is { Length: 0 })
            {
                Throw("empty Unicode property class");
            }

            name = normalizedName is null ? Pattern[nameStart..nameEnd] : normalizedName;
            _index = nameEnd + 1;
        }
        else
        {
            if (_index >= Pattern.Length)
            {
                Throw("missing Unicode property class name");
            }

            name = Pattern.Slice(_index, 1);
            _index++;
        }

        if (RegexUnicodePropertyNames.NameEquals(name, "any"))
        {
            if (!negated)
            {
                return new RegexAtomNode(RegexSyntaxKind.AnyClass, ReadOnlyMemory<byte>.Empty, position);
            }

            var emptyProperty = new RegexUnicodeProperty(
                RegexUnicodePropertyFamily.Property,
                RegexUnicodePropertyKind.None,
                RegexUnicodeScriptKind.None);
            return new RegexAtomNode(
                RegexSyntaxKind.UnicodePropertyClass,
                ReadOnlyMemory<byte>.Empty,
                position,
                unicodeProperty: emptyProperty);
        }

        if (!RegexUnicodePropertyNames.TryGetProperty(name, out RegexUnicodeProperty? property))
        {
            Throw("Unicode property not found");
        }

        ReadOnlyMemory<byte> value = property!.Family == RegexUnicodePropertyFamily.Property &&
            property.PropertyKind <= (RegexUnicodePropertyKind)byte.MaxValue
                ? new[] { (byte)property.PropertyKind }
                : ReadOnlyMemory<byte>.Empty;
        return new RegexAtomNode(
            negated ? RegexSyntaxKind.NotUnicodePropertyClass : RegexSyntaxKind.UnicodePropertyClass,
            value,
            position,
            unicodeProperty: property);
    }

    private bool TryParseNamedWordBoundary(out RegexSyntaxKind boundaryKind)
    {
        if (_index + 12 <= Pattern.Length && Pattern.Slice(_index, 12).SequenceEqual("{start-half}"u8))
        {
            _index += 12;
            boundaryKind = RegexSyntaxKind.WordStartHalfBoundary;
            return true;
        }

        if (_index + 10 <= Pattern.Length && Pattern.Slice(_index, 10).SequenceEqual("{end-half}"u8))
        {
            _index += 10;
            boundaryKind = RegexSyntaxKind.WordEndHalfBoundary;
            return true;
        }

        if (_index + 7 <= Pattern.Length && Pattern.Slice(_index, 7).SequenceEqual("{start}"u8))
        {
            _index += 7;
            boundaryKind = RegexSyntaxKind.WordStartBoundary;
            return true;
        }

        if (_index + 5 <= Pattern.Length && Pattern.Slice(_index, 5).SequenceEqual("{end}"u8))
        {
            _index += 5;
            boundaryKind = RegexSyntaxKind.WordEndBoundary;
            return true;
        }

        boundaryKind = RegexSyntaxKind.WordBoundary;
        return false;
    }

    private RegexAtomNode ParseHexEscape(int position, byte escaped)
    {
        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }

        RegexLiteralKind literalKind;
        int scalar;
        if (_index < Pattern.Length && Pattern[_index] == (byte)'{')
        {
            literalKind = RegexLiteralKind.HexBrace;
            scalar = ParseBracedHexScalar();
        }
        else
        {
            int digits = escaped switch
            {
                (byte)'x' => 2,
                (byte)'u' => 4,
                _ => 8,
            };
            literalKind = escaped switch
            {
                (byte)'x' => RegexLiteralKind.HexFixed,
                (byte)'u' => RegexLiteralKind.UnicodeShort,
                _ => RegexLiteralKind.UnicodeLong,
            };
            scalar = ParseFixedHexScalar(digits);
        }

        if (!Rune.IsValid(scalar))
        {
            Throw("hexadecimal escape is not a Unicode scalar value");
        }

        return CreateEscapedLiteral(position, scalar, literalKind);
    }

    private int ParseBracedHexScalar()
    {
        _index++;
        int value = 0;
        int digits = 0;
        while (true)
        {
            if (_extendedMode)
            {
                SkipExtendedPatternWhitespace();
            }

            if (_index >= Pattern.Length || Pattern[_index] == (byte)'}')
            {
                break;
            }

            if (!TryGetHexValue(Pattern[_index], out int digit))
            {
                Throw("invalid hexadecimal escape");
            }

            if (value > (0x10FFFF - digit) / 16)
            {
                Throw("hexadecimal escape is not a Unicode scalar value");
            }

            value = (value * 16) + digit;
            digits++;
            _index++;
        }

        if (digits == 0)
        {
            Throw("hexadecimal escape is empty");
        }

        if (_index >= Pattern.Length || Pattern[_index] != (byte)'}')
        {
            Throw("unclosed hexadecimal escape");
        }

        _index++;
        return value;
    }

    private int ParseFixedHexScalar(int digitCount)
    {
        int value = 0;
        for (int digitIndex = 0; digitIndex < digitCount; digitIndex++)
        {
            if (_extendedMode)
            {
                SkipExtendedPatternWhitespace();
            }

            int digit = 0;
            if (_index >= Pattern.Length || !TryGetHexValue(Pattern[_index], out digit))
            {
                Throw("invalid hexadecimal escape");
            }

            value = (value * 16) + digit;
            _index++;
        }

        return value;
    }

    private static RegexAtomNode CreateEscapedLiteral(int position, int scalar, RegexLiteralKind literalKind)
    {
        byte[] value = EncodeScalar(scalar);
        return new RegexAtomNode(
            RegexSyntaxKind.Literal,
            value,
            position,
            scalar,
            literalKind);
    }

    private static byte[] EncodeScalar(int scalar)
    {
        Span<byte> buffer = stackalloc byte[4];
        int length = new Rune(scalar).EncodeToUtf8(buffer);
        return buffer[..length].ToArray();
    }

    private static bool IsEscapablePunctuation(byte value)
    {
        return value <= 0x7F &&
            !IsAsciiDigitByte(value) &&
            !(value is >= (byte)'A' and <= (byte)'Z') &&
            !(value is >= (byte)'a' and <= (byte)'z') &&
            value != (byte)'<' &&
            value != (byte)'>';
    }

    private static bool IsRegexMetaByte(byte value)
    {
        return value is (byte)'\\'
            or (byte)'.'
            or (byte)'+'
            or (byte)'*'
            or (byte)'?'
            or (byte)'('
            or (byte)')'
            or (byte)'|'
            or (byte)'['
            or (byte)']'
            or (byte)'{'
            or (byte)'}'
            or (byte)'^'
            or (byte)'$'
            or (byte)'#'
            or (byte)'&'
            or (byte)'-'
            or (byte)'~';
    }

    private bool TryParseRepetition(RegexSyntaxNode child, out RegexSyntaxNode repetition)
    {
        repetition = child;
        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }

        if (_index >= Pattern.Length)
        {
            return false;
        }

        int position = _index;
        int minimum;
        int? maximum;
        byte token = Pattern[_index];
        if (token == (byte)'?')
        {
            ThrowIfInlineFlagsRepeat(child);
            minimum = 0;
            maximum = 1;
            _index++;
        }
        else if (token == (byte)'*')
        {
            ThrowIfInlineFlagsRepeat(child);
            minimum = 0;
            maximum = null;
            _index++;
        }
        else if (token == (byte)'+')
        {
            ThrowIfInlineFlagsRepeat(child);
            minimum = 1;
            maximum = null;
            _index++;
        }
        else if (token == (byte)'{')
        {
            ParseCountedRepetition(out minimum, out maximum);
            ThrowIfInlineFlagsRepeat(child);
        }
        else
        {
            return false;
        }

        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }

        bool lazy = false;
        if (_index < Pattern.Length && Pattern[_index] == (byte)'?')
        {
            lazy = true;
            _index++;
        }

        repetition = new RegexRepetitionNode(child, minimum, maximum, lazy, position);
        return true;
    }

    private void ThrowIfInlineFlagsRepeat(RegexSyntaxNode child)
    {
        if (child.Kind == RegexSyntaxKind.InlineFlags)
        {
            Throw("repetition operator missing expression");
        }
    }

    private void SkipExtendedPatternWhitespace()
    {
        while (_index < Pattern.Length)
        {
            byte value = Pattern[_index];
            if (IsExtendedWhitespaceByte(value))
            {
                _index++;
                continue;
            }

            if (value == (byte)'#')
            {
                _index++;
                while (_index < Pattern.Length && Pattern[_index] != (byte)'\n')
                {
                    _index++;
                }

                continue;
            }

            break;
        }
    }

    private void ParseCountedRepetition(out int minimum, out int? maximum)
    {
        _index++;
        if (_extendedMode)
        {
            SkipExtendedPatternWhitespace();
        }

        if (!TryReadDecimal(out minimum))
        {
            Throw("repetition quantifier expects a valid decimal");
        }

        maximum = minimum;
        if (_index < Pattern.Length && Pattern[_index] == (byte)',')
        {
            _index++;
            if (_extendedMode)
            {
                SkipExtendedPatternWhitespace();
            }

            maximum = null;
            if (_index < Pattern.Length && IsAsciiDigitByte(Pattern[_index]))
            {
                if (!TryReadDecimal(out int parsedMaximum))
                {
                    Throw("invalid repetition maximum");
                }

                maximum = parsedMaximum;
            }
        }

        if (_index >= Pattern.Length || Pattern[_index] != (byte)'}')
        {
            Throw("unclosed repetition quantifier");
        }

        if (maximum.HasValue && maximum.Value < minimum)
        {
            Throw("repetition maximum is smaller than minimum");
        }

        _index++;
    }

    private bool TryReadDecimal(out int value)
    {
        value = 0;
        int start = _index;
        while (_index < Pattern.Length && IsAsciiDigitByte(Pattern[_index]))
        {
            int digit = Pattern[_index] - (byte)'0';
            if (value > (int.MaxValue - digit) / 10)
            {
                value = int.MaxValue;
            }
            else
            {
                value = (value * 10) + digit;
            }

            _index++;
            if (_extendedMode)
            {
                SkipExtendedPatternWhitespace();
            }
        }

        return _index > start;
    }

    private static bool TryGetHexValue(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static bool IsRegexFlagByte(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'R' or (byte)'s' or (byte)'U' or (byte)'u' or (byte)'x';
    }

    private static bool IsAsciiDigitByte(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
    }

    private static bool IsExtendedWhitespaceByte(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0x0b;
    }

    private static bool IsCaptureNameByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';
    }

    private static bool IsCaptureNameStartByte(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value == (byte)'_';
    }

    [DoesNotReturn]
    private void Throw(string message)
    {
        throw new FormatException($"{message} at byte offset {_index}");
    }
}
