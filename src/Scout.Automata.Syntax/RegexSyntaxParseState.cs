using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

internal sealed class RegexSyntaxParseState
{
    private readonly ReadOnlyMemory<byte> pattern;
    private int captureCount;
    private bool extendedMode;
    private int index;

    public RegexSyntaxParseState(ReadOnlyMemory<byte> pattern)
    {
        this.pattern = pattern;
    }

    private ReadOnlySpan<byte> Pattern => pattern.Span;

    public RegexSyntaxTree Parse()
    {
        RegexSyntaxNode root = ParseAlternation(stopAtGroupEnd: false);
        if (index != Pattern.Length)
        {
            Throw("unexpected trailing token");
        }

        return new RegexSyntaxTree(pattern, root, captureCount);
    }

    private RegexSyntaxNode ParseAlternation(bool stopAtGroupEnd)
    {
        int position = index;
        var alternatives = new List<RegexSyntaxNode>();
        while (true)
        {
            alternatives.Add(ParseSequence(stopAtGroupEnd));
            if (index >= Pattern.Length || Pattern[index] != (byte)'|')
            {
                break;
            }

            index++;
        }

        return alternatives.Count == 1
            ? alternatives[0]
            : new RegexAlternationNode(alternatives, position);
    }

    private RegexSyntaxNode ParseSequence(bool stopAtGroupEnd)
    {
        int position = index;
        var nodes = new List<RegexSyntaxNode>();
        while (index < Pattern.Length)
        {
            if (extendedMode)
            {
                SkipExtendedPatternWhitespace();
                if (index >= Pattern.Length)
                {
                    break;
                }
            }

            byte token = Pattern[index];
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
        int position = index;
        byte token = Pattern[index++];
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
            _ => new RegexAtomNode(RegexSyntaxKind.Literal, new[] { token }, position),
        };
    }

    private RegexSyntaxNode ParseGroup(int position)
    {
        if (index < Pattern.Length && Pattern[index] == (byte)'?')
        {
            index++;
            if (index < Pattern.Length && Pattern[index] == (byte)':')
            {
                index++;
                return ParseGroupBody(position, capturing: false, captureName: null, enabledFlags: string.Empty, disabledFlags: string.Empty);
            }

            if (TryParseNamedCapturePrefix(out string? captureName))
            {
                return ParseGroupBody(position, capturing: true, captureName, enabledFlags: string.Empty, disabledFlags: string.Empty);
            }

            ParseInlineFlags(out string enabledFlags, out string disabledFlags, out bool scoped);
            if (scoped)
            {
                bool previousExtendedMode = extendedMode;
                ApplyParseFlags(enabledFlags, disabledFlags);
                try
                {
                    return ParseGroupBody(position, capturing: false, captureName: null, enabledFlags, disabledFlags);
                }
                finally
                {
                    extendedMode = previousExtendedMode;
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
        int captureIndex = 0;
        if (capturing)
        {
            captureCount++;
            captureIndex = captureCount;
        }

        RegexSyntaxNode child = ParseAlternation(stopAtGroupEnd: true);
        if (index >= Pattern.Length || Pattern[index] != (byte)')')
        {
            Throw("unclosed group");
        }

        index++;
        return new RegexGroupNode(
            capturing ? RegexSyntaxKind.CapturingGroup : RegexSyntaxKind.NonCapturingGroup,
            child,
            captureIndex,
            captureName,
            enabledFlags,
            disabledFlags,
            position);
    }

    private void ApplyParseFlags(string enabledFlags, string disabledFlags)
    {
        for (int index = 0; index < enabledFlags.Length; index++)
        {
            if (enabledFlags[index] == 'x')
            {
                extendedMode = true;
            }
        }

        for (int index = 0; index < disabledFlags.Length; index++)
        {
            if (disabledFlags[index] == 'x')
            {
                extendedMode = false;
            }
        }
    }

    private bool TryParseNamedCapturePrefix(out string? captureName)
    {
        captureName = null;
        int nameStart;
        if (index + 1 < Pattern.Length && Pattern[index] == (byte)'P' && Pattern[index + 1] == (byte)'<')
        {
            nameStart = index + 2;
        }
        else if (index < Pattern.Length && Pattern[index] == (byte)'<')
        {
            nameStart = index + 1;
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
        index = nameEnd + 1;
        return true;
    }

    private void ParseInlineFlags(out string enabledFlags, out string disabledFlags, out bool scoped)
    {
        var enabled = new List<byte>();
        var disabled = new List<byte>();
        bool disabling = false;
        while (index < Pattern.Length)
        {
            byte token = Pattern[index];
            if (token == (byte)'-')
            {
                disabling = true;
                index++;
                continue;
            }

            if (token == (byte)':')
            {
                if (enabled.Count == 0 && disabled.Count == 0)
                {
                    Throw("missing inline flags");
                }

                index++;
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

                index++;
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

            index++;
        }

        Throw("unclosed inline flags");
        enabledFlags = string.Empty;
        disabledFlags = string.Empty;
        scoped = false;
    }

    private RegexSyntaxNode ParseClass(int position)
    {
        int expressionStart = index;
        while (index < Pattern.Length)
        {
            if (Pattern[index] == (byte)'\\')
            {
                index += 2;
                continue;
            }

            if (Pattern[index] == (byte)'[' &&
                index + 1 < Pattern.Length &&
                Pattern[index + 1] == (byte)':' &&
                TryFindPosixClassEnd(index + 2, out int posixClassEnd))
            {
                index = posixClassEnd + 1;
                continue;
            }

            if (Pattern[index] == (byte)']')
            {
                ReadOnlyMemory<byte> expression = pattern.Slice(expressionStart, index - expressionStart);
                index++;
                return new RegexAtomNode(RegexSyntaxKind.CharacterClass, expression, position);
            }

            index++;
        }

        Throw("unclosed character class");
        return new RegexEmptyNode(position);
    }

    private RegexSyntaxNode ParseEscape(int position)
    {
        if (index >= Pattern.Length)
        {
            Throw("escape at end of pattern");
        }

        byte escaped = Pattern[index++];
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
        int start = index;
        ReadOnlySpan<byte> name;
        if (index < Pattern.Length && Pattern[index] == (byte)'{')
        {
            int nameStart = index + 1;
            int nameEnd = nameStart;
            while (nameEnd < Pattern.Length && Pattern[nameEnd] != (byte)'}')
            {
                nameEnd++;
            }

            if (nameEnd >= Pattern.Length || nameEnd == nameStart)
            {
                index = start;
                return false;
            }

            name = Pattern[nameStart..nameEnd];
            index = nameEnd + 1;
        }
        else
        {
            if (index >= Pattern.Length)
            {
                return false;
            }

            name = Pattern.Slice(index, 1);
            index++;
        }

        if (!negated && PropertyNameEquals(name, "any"))
        {
            node = new RegexAtomNode(RegexSyntaxKind.AnyClass, ReadOnlyMemory<byte>.Empty, position);
            return true;
        }

        if (!TryGetUnicodePropertyKind(name, out RegexUnicodePropertyKind propertyKind))
        {
            index = start;
            return false;
        }

        node = new RegexAtomNode(
            negated ? RegexSyntaxKind.NotUnicodePropertyClass : RegexSyntaxKind.UnicodePropertyClass,
            new[] { (byte)propertyKind },
            position);
        return true;
    }

    private static bool TryGetUnicodePropertyKind(ReadOnlySpan<byte> name, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.Letter;
        if (PropertyNameEquals(name, "lc") || PropertyNameEquals(name, "casedletter"))
        {
            propertyKind = RegexUnicodePropertyKind.CasedLetter;
            return true;
        }

        if (PropertyNameEquals(name, "pe") || PropertyNameEquals(name, "closepunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ClosePunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "pc") || PropertyNameEquals(name, "connectorpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ConnectorPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "cc") || PropertyNameEquals(name, "control"))
        {
            propertyKind = RegexUnicodePropertyKind.Control;
            return true;
        }

        if (PropertyNameEquals(name, "sc") || PropertyNameEquals(name, "currencysymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.CurrencySymbol;
            return true;
        }

        if (PropertyNameEquals(name, "pd") || PropertyNameEquals(name, "dashpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.DashPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "nd") || PropertyNameEquals(name, "decimalnumber") || PropertyNameEquals(name, "digit"))
        {
            propertyKind = RegexUnicodePropertyKind.DecimalNumber;
            return true;
        }

        if (PropertyNameEquals(name, "me") || PropertyNameEquals(name, "enclosingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.EnclosingMark;
            return true;
        }

        if (PropertyNameEquals(name, "pf") || PropertyNameEquals(name, "finalpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.FinalPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "cf") || PropertyNameEquals(name, "format"))
        {
            propertyKind = RegexUnicodePropertyKind.Format;
            return true;
        }

        if (PropertyNameEquals(name, "pi") || PropertyNameEquals(name, "initialpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.InitialPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "l") || PropertyNameEquals(name, "letter"))
        {
            propertyKind = RegexUnicodePropertyKind.Letter;
            return true;
        }

        if (PropertyNameEquals(name, "nl") || PropertyNameEquals(name, "letternumber"))
        {
            propertyKind = RegexUnicodePropertyKind.LetterNumber;
            return true;
        }

        if (PropertyNameEquals(name, "zl") || PropertyNameEquals(name, "lineseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.LineSeparator;
            return true;
        }

        if (PropertyNameEquals(name, "ll") || PropertyNameEquals(name, "lowercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.LowercaseLetter;
            return true;
        }

        if (PropertyNameEquals(name, "m") || PropertyNameEquals(name, "mark"))
        {
            propertyKind = RegexUnicodePropertyKind.Mark;
            return true;
        }

        if (PropertyNameEquals(name, "math"))
        {
            propertyKind = RegexUnicodePropertyKind.Math;
            return true;
        }

        if (PropertyNameEquals(name, "sm") || PropertyNameEquals(name, "mathsymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.MathSymbol;
            return true;
        }

        if (PropertyNameEquals(name, "lm") || PropertyNameEquals(name, "modifierletter"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierLetter;
            return true;
        }

        if (PropertyNameEquals(name, "sk") || PropertyNameEquals(name, "modifiersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierSymbol;
            return true;
        }

        if (PropertyNameEquals(name, "mn") || PropertyNameEquals(name, "nonspacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.NonspacingMark;
            return true;
        }

        if (PropertyNameEquals(name, "emoji"))
        {
            propertyKind = RegexUnicodePropertyKind.Emoji;
            return true;
        }

        if (PropertyNameEquals(name, "extendedpictographic"))
        {
            propertyKind = RegexUnicodePropertyKind.ExtendedPictographic;
            return true;
        }

        if (PropertyNameEquals(name, "n") || PropertyNameEquals(name, "number"))
        {
            propertyKind = RegexUnicodePropertyKind.Number;
            return true;
        }

        if (PropertyNameEquals(name, "ps") || PropertyNameEquals(name, "openpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OpenPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "c") || PropertyNameEquals(name, "other"))
        {
            propertyKind = RegexUnicodePropertyKind.Other;
            return true;
        }

        if (PropertyNameEquals(name, "lo") || PropertyNameEquals(name, "otherletter"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherLetter;
            return true;
        }

        if (PropertyNameEquals(name, "no") || PropertyNameEquals(name, "othernumber"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherNumber;
            return true;
        }

        if (PropertyNameEquals(name, "po") || PropertyNameEquals(name, "otherpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherPunctuation;
            return true;
        }

        if (PropertyNameEquals(name, "so") || PropertyNameEquals(name, "othersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherSymbol;
            return true;
        }

        if (PropertyNameEquals(name, "zp") || PropertyNameEquals(name, "paragraphseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.ParagraphSeparator;
            return true;
        }

        if (PropertyNameEquals(name, "co") || PropertyNameEquals(name, "privateuse"))
        {
            propertyKind = RegexUnicodePropertyKind.PrivateUse;
            return true;
        }

        if (PropertyNameEquals(name, "p") || PropertyNameEquals(name, "punctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.Punctuation;
            return true;
        }

        if (PropertyNameEquals(name, "z") || PropertyNameEquals(name, "separator"))
        {
            propertyKind = RegexUnicodePropertyKind.Separator;
            return true;
        }

        if (PropertyNameEquals(name, "zs") || PropertyNameEquals(name, "spaceseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.SpaceSeparator;
            return true;
        }

        if (PropertyNameEquals(name, "mc") || PropertyNameEquals(name, "spacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.SpacingMark;
            return true;
        }

        if (PropertyNameEquals(name, "s") || PropertyNameEquals(name, "symbol"))
        {
            propertyKind = RegexUnicodePropertyKind.Symbol;
            return true;
        }

        if (PropertyNameEquals(name, "lt") || PropertyNameEquals(name, "titlecaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.TitlecaseLetter;
            return true;
        }

        if (PropertyNameEquals(name, "cn") || PropertyNameEquals(name, "unassigned"))
        {
            propertyKind = RegexUnicodePropertyKind.Unassigned;
            return true;
        }

        if (PropertyNameEquals(name, "lu") || PropertyNameEquals(name, "uppercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.UppercaseLetter;
            return true;
        }

        return false;
    }

    private static bool PropertyNameEquals(ReadOnlySpan<byte> name, string expected)
    {
        int expectedIndex = 0;
        for (int index = 0; index < name.Length; index++)
        {
            byte value = name[index];
            if (value is (byte)'_' or (byte)'-' or (byte)' ')
            {
                continue;
            }

            if (expectedIndex >= expected.Length)
            {
                return false;
            }

            if (value is >= (byte)'A' and <= (byte)'Z')
            {
                value = (byte)(value + 32);
            }

            if (value != expected[expectedIndex])
            {
                return false;
            }

            expectedIndex++;
        }

        return expectedIndex == expected.Length;
    }

    private bool TryParseNamedWordBoundary(out RegexSyntaxKind boundaryKind)
    {
        if (index + 12 <= Pattern.Length && Pattern.Slice(index, 12).SequenceEqual("{start-half}"u8))
        {
            index += 12;
            boundaryKind = RegexSyntaxKind.WordStartHalfBoundary;
            return true;
        }

        if (index + 10 <= Pattern.Length && Pattern.Slice(index, 10).SequenceEqual("{end-half}"u8))
        {
            index += 10;
            boundaryKind = RegexSyntaxKind.WordEndHalfBoundary;
            return true;
        }

        if (index + 7 <= Pattern.Length && Pattern.Slice(index, 7).SequenceEqual("{start}"u8))
        {
            index += 7;
            boundaryKind = RegexSyntaxKind.WordStartBoundary;
            return true;
        }

        if (index + 5 <= Pattern.Length && Pattern.Slice(index, 5).SequenceEqual("{end}"u8))
        {
            index += 5;
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
            if (index + 1 < Pattern.Length &&
                TryReadHexByte(Pattern[index], Pattern[index + 1], out byte literal))
            {
                index += 2;
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
        if (index >= Pattern.Length || Pattern[index] != (byte)'{')
        {
            return false;
        }

        index++;
        int value = 0;
        int digits = 0;
        while (index < Pattern.Length && Pattern[index] != (byte)'}')
        {
            if (!TryGetHexValue(Pattern[index], out int digit))
            {
                Throw("invalid hexadecimal escape");
            }

            value = (value * 16) + digit;
            if (value > byte.MaxValue)
            {
                Throw("hexadecimal escape exceeds one byte");
            }

            digits++;
            index++;
        }

        if (digits == 0 || index >= Pattern.Length || Pattern[index] != (byte)'}')
        {
            Throw("unclosed hexadecimal escape");
        }

        index++;
        escapedNode = new RegexAtomNode(RegexSyntaxKind.Literal, new[] { (byte)value }, position);
        return true;
    }

    private bool TryParseRepetition(RegexSyntaxNode child, out RegexSyntaxNode repetition)
    {
        repetition = child;
        if (index >= Pattern.Length)
        {
            return false;
        }

        int position = index;
        int minimum;
        int? maximum;
        byte token = Pattern[index];
        if (token == (byte)'?')
        {
            minimum = 0;
            maximum = 1;
            index++;
        }
        else if (token == (byte)'*')
        {
            minimum = 0;
            maximum = null;
            index++;
        }
        else if (token == (byte)'+')
        {
            minimum = 1;
            maximum = null;
            index++;
        }
        else if (token == (byte)'{')
        {
            if (!TryParseCountedRepetition(out minimum, out maximum))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        bool lazy = false;
        if (index < Pattern.Length && Pattern[index] == (byte)'?')
        {
            lazy = true;
            index++;
        }

        repetition = new RegexRepetitionNode(child, minimum, maximum, lazy, position);
        return true;
    }

    private void SkipExtendedPatternWhitespace()
    {
        while (index < Pattern.Length)
        {
            byte value = Pattern[index];
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

            break;
        }
    }

    private bool TryParseCountedRepetition(out int minimum, out int? maximum)
    {
        int start = index;
        index++;
        if (!TryReadDecimal(out minimum))
        {
            index = start;
            maximum = null;
            return false;
        }

        maximum = minimum;
        if (index < Pattern.Length && Pattern[index] == (byte)',')
        {
            index++;
            maximum = null;
            if (index < Pattern.Length && IsAsciiDigitByte(Pattern[index]))
            {
                if (!TryReadDecimal(out int parsedMaximum))
                {
                    Throw("invalid repetition maximum");
                }

                maximum = parsedMaximum;
            }
        }

        if (index >= Pattern.Length || Pattern[index] != (byte)'}')
        {
            index = start;
            return false;
        }

        if (maximum.HasValue && maximum.Value < minimum)
        {
            Throw("repetition maximum is smaller than minimum");
        }

        index++;
        return true;
    }

    private bool TryReadDecimal(out int value)
    {
        value = 0;
        int start = index;
        while (index < Pattern.Length && IsAsciiDigitByte(Pattern[index]))
        {
            int digit = Pattern[index] - (byte)'0';
            if (value > (int.MaxValue - digit) / 10)
            {
                value = int.MaxValue;
            }
            else
            {
                value = (value * 10) + digit;
            }

            index++;
        }

        return index > start;
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
        throw new FormatException($"{message} at byte offset {index}");
    }
}
