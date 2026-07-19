using System.Text;

namespace Scout;

/// <summary>
/// Extracts an exact, finite literal language from authoritative regex syntax.
/// </summary>
/// <remarks>
/// The extractor preserves alternation and repetition preference order while bounding
/// character-class expansion, repetition, literal length, and total language size.
/// Unsupported or over-limit syntax falls back to the general regex automaton.
/// </remarks>
internal sealed class RegexFiniteLiteralExtractor(RegexCompileOptions options)
{
    private const int ClassLimit = 10;
    private const int RepeatLimit = 10;
    private const int LiteralLengthLimit = 100;
    private const int TotalLimit = 250;

    private readonly RegexCompileOptions _options = options;
    private bool? _caseInsensitive;
    private bool? _unicodeCaseFolding;
    private bool _containsCharacterClass;

    /// <summary>
    /// Attempts to extract the complete literal language represented by a syntax tree.
    /// </summary>
    /// <param name="root">The authoritative syntax root.</param>
    /// <param name="options">The root compilation options.</param>
    /// <param name="literals">Receives the finite literals in regex preference order.</param>
    /// <param name="caseInsensitive">Receives the common case-insensitive mode for consuming atoms.</param>
    /// <param name="unicodeCaseFolding">Receives the common Unicode case-folding mode for consuming atoms.</param>
    /// <param name="containsCharacterClass">Receives whether extraction expanded a character-class atom.</param>
    /// <returns>
    /// <see langword="true" /> when the complete language is finite, exact, and within the extraction limits;
    /// otherwise, <see langword="false" />.
    /// </returns>
    public static bool TryExtract(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out List<byte[]> literals,
        out bool? caseInsensitive,
        out bool? unicodeCaseFolding,
        out bool containsCharacterClass)
    {
        var extractor = new RegexFiniteLiteralExtractor(options);
        bool extracted = extractor.TryExtractNode(root, extractor._options, out literals);
        caseInsensitive = extractor._caseInsensitive;
        unicodeCaseFolding = extractor._unicodeCaseFolding;
        containsCharacterClass = extractor._containsCharacterClass;
        return extracted;
    }

    private bool TryExtractNode(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        switch (node)
        {
            case RegexEmptyNode:
                literals = [Array.Empty<byte>()];
                return true;
            case RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom:
                return TryExtractLiteral(atom, options, out literals);
            case RegexAtomNode atom:
                return TryExtractClass(atom, options, out literals);
            case RegexSequenceNode sequence:
                return TryExtractSequence(sequence, options, out literals);
            case RegexAlternationNode alternation:
                return TryExtractAlternation(alternation, options, out literals);
            case RegexGroupNode group:
                return TryExtractNode(
                    group.Child,
                    options.Apply(group.EnabledFlags, group.DisabledFlags),
                    out literals);
            case RegexInlineFlagsNode:
                literals = [Array.Empty<byte>()];
                return true;
            case RegexRepetitionNode repetition:
                return TryExtractRepetition(repetition, options, out literals);
            default:
                literals = [];
                return false;
        }
    }

    private bool TryExtractLiteral(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        if (!TrySetCaseMode(options))
        {
            literals = [];
            return false;
        }

        if (atom.LiteralKind == RegexLiteralKind.HexFixed &&
            !options.Utf8 &&
            !options.UnicodeClasses)
        {
            if (atom.Scalar is < 0 or > byte.MaxValue)
            {
                literals = [];
                return false;
            }

            literals = [[(byte)atom.Scalar]];
            return true;
        }

        if (atom.Value.Length > LiteralLengthLimit)
        {
            literals = [];
            return false;
        }

        literals = [atom.Value.ToArray()];
        return true;
    }

    private bool TryExtractClass(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        _containsCharacterClass = true;
        literals = [];
        if (!RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(atom, options, out List<RegexScalarRange> ranges) ||
            !TrySetCaseMode(options))
        {
            return false;
        }

        int scalarCount = 0;
        for (int index = 0; index < ranges.Count; index++)
        {
            RegexScalarRange range = ranges[index];
            long rangeLength = (long)range.End - range.Start + 1;
            if (rangeLength <= 0 || scalarCount + rangeLength > ClassLimit)
            {
                literals = [];
                return false;
            }

            scalarCount += (int)rangeLength;
        }

        Span<byte> utf8 = stackalloc byte[4];
        for (int rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
        {
            RegexScalarRange range = ranges[rangeIndex];
            for (int scalar = range.Start; scalar <= range.End; scalar++)
            {
                byte[] literal;
                if (options.Utf8)
                {
                    int length = new Rune(scalar).EncodeToUtf8(utf8);
                    literal = utf8[..length].ToArray();
                }
                else
                {
                    if (scalar > 0x7F)
                    {
                        literals = [];
                        return false;
                    }

                    literal = [(byte)scalar];
                }

                if (!TryAddDistinct(literals, literal))
                {
                    literals = [];
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryExtractSequence(
        RegexSequenceNode sequence,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        literals = [Array.Empty<byte>()];
        RegexCompileOptions currentOptions = options;
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = sequence.Nodes[index];
            if (child is RegexInlineFlagsNode flags)
            {
                currentOptions = currentOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (!TryExtractNode(child, currentOptions, out List<byte[]> suffixes) ||
                !TryCross(literals, suffixes, out literals))
            {
                literals = [];
                return false;
            }
        }

        return true;
    }

    private bool TryExtractAlternation(
        RegexAlternationNode alternation,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        literals = [];
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (!TryExtractNode(alternation.Alternatives[index], options, out List<byte[]> branch) ||
                !TryUnion(literals, branch))
            {
                literals = [];
                return false;
            }
        }

        return true;
    }

    private bool TryExtractRepetition(
        RegexRepetitionNode repetition,
        RegexCompileOptions options,
        out List<byte[]> literals)
    {
        literals = [];
        if (repetition.Maximum is not int maximum ||
            maximum < repetition.Minimum ||
            maximum > RepeatLimit)
        {
            return false;
        }

        if (maximum == 0)
        {
            literals.Add(Array.Empty<byte>());
            return true;
        }

        if (!TryExtractNode(repetition.Child, options, out List<byte[]> repeatedOnce))
        {
            return false;
        }

        bool lazy = repetition.Lazy ^ options.SwapGreed;
        if (lazy)
        {
            for (int count = repetition.Minimum; count <= maximum; count++)
            {
                if (!TryAppendRepetition(repeatedOnce, count, literals))
                {
                    literals = [];
                    return false;
                }
            }
        }
        else
        {
            for (int count = maximum; count >= repetition.Minimum; count--)
            {
                if (!TryAppendRepetition(repeatedOnce, count, literals))
                {
                    literals = [];
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryAppendRepetition(
        List<byte[]> repeatedOnce,
        int count,
        List<byte[]> destination)
    {
        List<byte[]> repeated = [Array.Empty<byte>()];
        for (int index = 0; index < count; index++)
        {
            if (!TryCross(repeated, repeatedOnce, out repeated))
            {
                return false;
            }
        }

        return TryUnion(destination, repeated);
    }

    private static bool TryCross(
        List<byte[]> prefixes,
        List<byte[]> suffixes,
        out List<byte[]> product)
    {
        product = [];
        if ((long)prefixes.Count * suffixes.Count > TotalLimit)
        {
            return false;
        }

        for (int prefixIndex = 0; prefixIndex < prefixes.Count; prefixIndex++)
        {
            byte[] prefix = prefixes[prefixIndex];
            for (int suffixIndex = 0; suffixIndex < suffixes.Count; suffixIndex++)
            {
                byte[] suffix = suffixes[suffixIndex];
                if (prefix.Length + suffix.Length > LiteralLengthLimit)
                {
                    product = [];
                    return false;
                }

                byte[] literal = new byte[prefix.Length + suffix.Length];
                prefix.CopyTo(literal, 0);
                suffix.CopyTo(literal, prefix.Length);
                if (!TryAddDistinct(product, literal))
                {
                    product = [];
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryUnion(List<byte[]> destination, List<byte[]> source)
    {
        for (int index = 0; index < source.Count; index++)
        {
            if (!TryAddDistinct(destination, source[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddDistinct(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return true;
            }
        }

        if (literals.Count >= TotalLimit)
        {
            return false;
        }

        literals.Add(literal);
        return true;
    }

    private bool TrySetCaseMode(RegexCompileOptions options)
    {
        if (_caseInsensitive.HasValue && _caseInsensitive.Value != options.CaseInsensitive)
        {
            return false;
        }

        _caseInsensitive = options.CaseInsensitive;
        if (!options.CaseInsensitive)
        {
            return true;
        }

        if (_unicodeCaseFolding.HasValue && _unicodeCaseFolding.Value != options.UnicodeClasses)
        {
            return false;
        }

        _unicodeCaseFolding = options.UnicodeClasses;
        return true;
    }
}
