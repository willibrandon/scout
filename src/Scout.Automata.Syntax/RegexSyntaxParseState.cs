using System.Buffers;
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
            if (TryParseRepetition(node, out RegexSyntaxNode repeated))
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
        if (token is (byte)'?' or (byte)'*' or (byte)'+')
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
            Rune.DecodeFromUtf8(Pattern[position..], out _, out int length) == OperationStatus.Done &&
            length > 1)
        {
            _index = position + length;
            return new RegexAtomNode(RegexSyntaxKind.Literal, Pattern.Slice(position, length).ToArray(), position);
        }

        return new RegexAtomNode(RegexSyntaxKind.Literal, new[] { token }, position);
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

    private RegexSyntaxNode ParseClass(int position)
    {
        int expressionStart = _index;
        if (_extendedMode)
        {
            var expression = new List<byte>();
            while (_index < Pattern.Length)
            {
                byte value = Pattern[_index];
                if (value == (byte)'\\' && _index + 1 < Pattern.Length)
                {
                    expression.Add(value);
                    expression.Add(Pattern[_index + 1]);
                    _index += 2;
                    continue;
                }

                if (value == (byte)'[' &&
                    _index + 1 < Pattern.Length &&
                    Pattern[_index + 1] == (byte)':' &&
                    TryFindPosixClassEnd(_index + 2, out int posixClassEnd))
                {
                    expression.AddRange(Pattern[_index..(posixClassEnd + 1)].ToArray());
                    _index = posixClassEnd + 1;
                    continue;
                }

                if (value == (byte)']')
                {
                    _index++;
                    return new RegexAtomNode(RegexSyntaxKind.CharacterClass, expression.ToArray(), position);
                }

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

                expression.Add(value);
                _index++;
            }

            Throw("unclosed character class");
            return new RegexEmptyNode(position);
        }

        while (_index < Pattern.Length)
        {
            if (Pattern[_index] == (byte)'\\')
            {
                _index += 2;
                continue;
            }

            if (Pattern[_index] == (byte)'[' &&
                _index + 1 < Pattern.Length &&
                Pattern[_index + 1] == (byte)':' &&
                TryFindPosixClassEnd(_index + 2, out int posixClassEnd))
            {
                _index = posixClassEnd + 1;
                continue;
            }

            if (Pattern[_index] == (byte)']')
            {
                ReadOnlyMemory<byte> expression = _pattern.Slice(expressionStart, _index - expressionStart);
                _index++;
                return new RegexAtomNode(RegexSyntaxKind.CharacterClass, expression, position);
            }

            _index++;
        }

        Throw("unclosed character class");
        return new RegexEmptyNode(position);
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

        if (TryParseByteEscape(position, escaped, out RegexSyntaxNode escapedNode))
        {
            return escapedNode;
        }

        if ((escaped == (byte)'p' || escaped == (byte)'P') &&
            TryParseUnicodePropertyClass(position, negated: escaped == (byte)'P', out RegexSyntaxNode propertyNode))
        {
            return propertyNode;
        }

        return escaped switch
        {
            (byte)'A' => new RegexAtomNode(RegexSyntaxKind.AbsoluteStartAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'z' => new RegexAtomNode(RegexSyntaxKind.AbsoluteEndAnchor, ReadOnlyMemory<byte>.Empty, position),
            (byte)'b' => new RegexAtomNode(RegexSyntaxKind.WordBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'B' => new RegexAtomNode(RegexSyntaxKind.NotWordBoundary, ReadOnlyMemory<byte>.Empty, position),
            (byte)'d' => new RegexAtomNode(RegexSyntaxKind.DigitClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'D' => new RegexAtomNode(RegexSyntaxKind.NotDigitClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'w' => new RegexAtomNode(RegexSyntaxKind.WordClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'W' => new RegexAtomNode(RegexSyntaxKind.NotWordClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'s' => new RegexAtomNode(RegexSyntaxKind.WhitespaceClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'S' => new RegexAtomNode(RegexSyntaxKind.NotWhitespaceClass, ReadOnlyMemory<byte>.Empty, position),
            (byte)'n' => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)'\n' }, position),
            (byte)'t' => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)'\t' }, position),
            (byte)'r' => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)'\r' }, position),
            (byte)'f' => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)'\f' }, position),
            _ => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { escaped }, position),
        };
    }

    private bool TryParseUnicodePropertyClass(int position, bool negated, out RegexSyntaxNode node)
    {
        node = new RegexEmptyNode(position);
        int start = _index;
        ReadOnlySpan<byte> name;
        if (_index < Pattern.Length && Pattern[_index] == (byte)'{')
        {
            int nameStart = _index + 1;
            int nameEnd = nameStart;
            while (nameEnd < Pattern.Length && Pattern[nameEnd] != (byte)'}')
            {
                nameEnd++;
            }

            if (nameEnd >= Pattern.Length || nameEnd == nameStart)
            {
                _index = start;
                return false;
            }

            name = Pattern[nameStart..nameEnd];
            _index = nameEnd + 1;
        }
        else
        {
            if (_index >= Pattern.Length)
            {
                return false;
            }

            name = Pattern.Slice(_index, 1);
            _index++;
        }

        if (!negated && RegexUnicodePropertyNames.NameEquals(name, "any"))
        {
            node = new RegexAtomNode(RegexSyntaxKind.AnyClass, ReadOnlyMemory<byte>.Empty, position);
            return true;
        }

        if (!RegexUnicodePropertyNames.TryGetKind(name, out RegexUnicodePropertyKind propertyKind))
        {
            _index = start;
            return false;
        }

        node = new RegexAtomNode(
            negated ? RegexSyntaxKind.NotUnicodePropertyClass : RegexSyntaxKind.UnicodePropertyClass,
            new[] { (byte)propertyKind },
            position);
        return true;
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

    private bool TryParseByteEscape(int position, byte escaped, out RegexSyntaxNode escapedNode)
    {
        escapedNode = new RegexEmptyNode(position);
        if (escaped == (byte)'x')
        {
            if (_index + 1 < Pattern.Length &&
                TryReadHexByte(Pattern[_index], Pattern[_index + 1], out byte literal))
            {
                _index += 2;
                escapedNode = new RegexAtomNode(RegexSyntaxKind.Literal, new[] { literal }, position);
                return true;
            }

            return TryParseBracedByteEscape(position, out escapedNode);
        }

        if (escaped == (byte)'u')
        {
            return TryParseBracedByteEscape(position, out escapedNode);
        }

        return false;
    }

    private bool TryParseBracedByteEscape(int position, out RegexSyntaxNode escapedNode)
    {
        escapedNode = new RegexEmptyNode(position);
        if (_index >= Pattern.Length || Pattern[_index] != (byte)'{')
        {
            return false;
        }

        _index++;
        int value = 0;
        int digits = 0;
        while (_index < Pattern.Length && Pattern[_index] != (byte)'}')
        {
            if (!TryGetHexValue(Pattern[_index], out int digit))
            {
                Throw("invalid hexadecimal escape");
            }

            value = (value * 16) + digit;
            if (value > byte.MaxValue)
            {
                Throw("hexadecimal escape exceeds one byte");
            }

            digits++;
            _index++;
        }

        if (digits == 0 || _index >= Pattern.Length || Pattern[_index] != (byte)'}')
        {
            Throw("unclosed hexadecimal escape");
        }

        _index++;
        escapedNode = new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)value }, position);
        return true;
    }

    private bool TryParseRepetition(RegexSyntaxNode child, out RegexSyntaxNode repetition)
    {
        repetition = child;
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
            if (!TryParseCountedRepetition(out minimum, out maximum))
            {
                return false;
            }

            ThrowIfInlineFlagsRepeat(child);
        }
        else
        {
            return false;
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

    private bool TryParseCountedRepetition(out int minimum, out int? maximum)
    {
        int start = _index;
        _index++;
        if (!TryReadDecimal(out minimum))
        {
            _index = start;
            maximum = null;
            return false;
        }

        maximum = minimum;
        if (_index < Pattern.Length && Pattern[_index] == (byte)',')
        {
            _index++;
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
            _index = start;
            return false;
        }

        if (maximum.HasValue && maximum.Value < minimum)
        {
            Throw("repetition maximum is smaller than minimum");
        }

        _index++;
        return true;
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
        }

        return _index > start;
    }

    private bool TryFindPosixClassEnd(int searchIndex, out int classEnd)
    {
        while (searchIndex + 1 < Pattern.Length)
        {
            if (Pattern[searchIndex] == (byte)':' && Pattern[searchIndex + 1] == (byte)']')
            {
                classEnd = searchIndex + 1;
                return true;
            }

            searchIndex++;
        }

        classEnd = -1;
        return false;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexValue(high, out int highValue) || !TryGetHexValue(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
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

    private void Throw(string message)
    {
        throw new FormatException($"{message} at byte offset {_index}");
    }
}
