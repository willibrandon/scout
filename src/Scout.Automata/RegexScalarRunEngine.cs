using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

internal sealed class RegexScalarRunEngine
{
    private const int MaxBoundedRepeat = 1024;
    private static readonly int[] UnicodeLowerOrUpperRanges = RegexUnicodeRangeCursor.MergeRanges(
        RegexUnicodeTables.GetBooleanPropertyRanges(RegexUnicodePropertyKind.Lowercase),
        RegexUnicodeTables.GetBooleanPropertyRanges(RegexUnicodePropertyKind.Uppercase));
    private static readonly byte[] UnicodeLowerOrUpperFirstBytes =
        RegexUnicodeRangeCursor.CreateFirstByteLookup(UnicodeLowerOrUpperRanges);

    private readonly RegexScalarRunAtom[] atoms;
    private readonly RegexCompileOptions options;
    private readonly int minimum;
    private readonly int maximum;
    private readonly bool lazy;
    private readonly bool unicodeLowerOrUpperFastPath;

    private RegexScalarRunEngine(
        RegexScalarRunAtom[] atoms,
        RegexCompileOptions options,
        int minimum,
        int maximum,
        bool lazy,
        bool unicodeLowerOrUpperFastPath)
    {
        this.atoms = atoms;
        this.options = options;
        this.minimum = minimum;
        this.maximum = maximum;
        this.lazy = lazy;
        this.unicodeLowerOrUpperFastPath = unicodeLowerOrUpperFastPath;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexScalarRunEngine? engine)
    {
        engine = null;
        if (!TryExtractScalarRunNode(root, options, out RegexSyntaxNode? scalarRunNode, out RegexCompileOptions effectiveOptions))
        {
            return false;
        }

        root = UnwrapTransparentGroups(scalarRunNode!);
        if (!effectiveOptions.UnicodeClasses ||
            root is not RegexRepetitionNode { Minimum: > 0, Maximum: { } maximum } repetition ||
            maximum > MaxBoundedRepeat ||
            !TryCollectScalarAtoms(repetition.Child, effectiveOptions, out RegexScalarRunAtom[] atoms))
        {
            return false;
        }

        bool unicodeLowerOrUpperFastPath = IsUnicodeLowerOrUpperFastPath(atoms, effectiveOptions);
        engine = new RegexScalarRunEngine(
            atoms,
            effectiveOptions,
            repetition.Minimum,
            maximum,
            repetition.Lazy,
            unicodeLowerOrUpperFastPath);
        return true;
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        for (int start = startOffset; start < haystack.Length; start++)
        {
            if (TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    public bool TryCountNonOverlapping(
        ReadOnlySpan<byte> haystack,
        int startAt,
        bool sumSpans,
        out long count,
        out long spanSum)
    {
        count = 0;
        spanSum = 0;
        if (!sumSpans)
        {
            return TryCountNonOverlappingCountOnly(haystack, startAt, ref count);
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        int currentScalars = 0;
        int currentBytes = 0;
        int runScalars = 0;
        int runBytes = 0;
        while (position < haystack.Length)
        {
            if (TryAtomMatchLength(haystack, position, out int scalarLength))
            {
                runScalars++;
                runBytes += scalarLength;
                currentScalars++;
                currentBytes += scalarLength;
                position += scalarLength;

                int selectedScalars = lazy ? minimum : maximum;
                if (currentScalars == selectedScalars)
                {
                    count++;
                    spanSum += currentBytes;
                    currentScalars = 0;
                    currentBytes = 0;
                }

                continue;
            }

            AddTrailingRun(runScalars, runBytes, currentScalars, currentBytes, ref count, ref spanSum);
            runScalars = 0;
            runBytes = 0;
            currentScalars = 0;
            currentBytes = 0;
            position = AdvanceAfterNonMatch(haystack, position);
        }

        AddTrailingRun(runScalars, runBytes, currentScalars, currentBytes, ref count, ref spanSum);
        return true;
    }

    private bool TryCountNonOverlappingCountOnly(ReadOnlySpan<byte> haystack, int startAt, ref long count)
    {
        if (atoms.Length == 1 && atoms[0].UnicodeLetterFastPath)
        {
            CountUnicodeLetterFastPath(haystack, startAt, ref count);
            return true;
        }

        if (unicodeLowerOrUpperFastPath)
        {
            CountUnicodeLowerOrUpperFastPath(haystack, startAt, ref count);
            return true;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        int currentScalars = 0;
        int selectedScalars = lazy ? minimum : maximum;
        while (position < haystack.Length)
        {
            if (TryAtomMatchLength(haystack, position, out int scalarLength))
            {
                currentScalars++;
                position += scalarLength;
                if (currentScalars == selectedScalars)
                {
                    count++;
                    currentScalars = 0;
                }

                continue;
            }

            if (currentScalars >= minimum)
            {
                count++;
            }

            currentScalars = 0;
            position = AdvanceAfterNonMatch(haystack, position);
        }

        if (currentScalars >= minimum)
        {
            count++;
        }

        return true;
    }

    private void CountUnicodeLetterFastPath(ReadOnlySpan<byte> haystack, int startAt, ref long count)
    {
        if (!lazy && minimum == 8 && maximum == 13)
        {
            CountUnicodeLetter8To13FastPath(haystack, startAt, ref count);
            return;
        }

        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position < haystack.Length)
        {
            int runScalars = 0;
            while (position < haystack.Length)
            {
                int cyrillicScalars = ConsumeFastCyrillicLetterPairs(haystack, ref position);
                if (cyrillicScalars != 0)
                {
                    runScalars += cyrillicScalars;
                    continue;
                }

                if (!TryUnicodeLetterFastPathMatchLength(haystack, position, out int scalarLength))
                {
                    break;
                }

                runScalars++;
                position += scalarLength;
            }

            AddScalarRunCountOnly(runScalars, ref count);
            if (position < haystack.Length)
            {
                position = AdvanceAfterNonMatch(haystack, position);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void CountUnicodeLowerOrUpperFastPath(ReadOnlySpan<byte> haystack, int startAt, ref long count)
    {
        var ranges = new RegexUnicodeRangeCursor(UnicodeLowerOrUpperRanges);
        int position = Math.Clamp(startAt, 0, haystack.Length);
        int runScalars = 0;
        while (position < haystack.Length)
        {
            bool matched = TryUnicodeLowerOrUpperFastPathMatchLength(haystack, position, ref ranges, out int scalarLength);
            if (matched)
            {
                runScalars++;
                position += scalarLength;
                continue;
            }

            if (runScalars >= minimum)
            {
                AddScalarRunCountOnly(runScalars, ref count);
            }

            runScalars = 0;
            position += scalarLength == 0 ? 1 : scalarLength;
        }

        if (runScalars >= minimum)
        {
            AddScalarRunCountOnly(runScalars, ref count);
        }
    }

    private void CountUnicodeLetter8To13FastPath(ReadOnlySpan<byte> haystack, int startAt, ref long count)
    {
        int position = Math.Clamp(startAt, 0, haystack.Length);
        while (position < haystack.Length)
        {
            int runScalars = 0;
            while (position < haystack.Length)
            {
                int cyrillicScalars = ConsumeFastCyrillicLetterPairs(haystack, ref position);
                if (cyrillicScalars != 0)
                {
                    runScalars += cyrillicScalars;
                    if (position >= haystack.Length)
                    {
                        break;
                    }
                }

                byte first = haystack[position];
                if (first <= 0x7F)
                {
                    if (!RegexSimpleSequenceSegment.IsAsciiLetter(first))
                    {
                        break;
                    }

                    runScalars++;
                    position++;
                    continue;
                }

                if (!TryUnicodeLetterFastPathMatchLength(haystack, position, out int scalarLength))
                {
                    break;
                }

                runScalars++;
                position += scalarLength;
            }

            AddScalarRun8To13CountOnly(runScalars, ref count);
            if (position < haystack.Length)
            {
                position = AdvanceAfterNonMatch(haystack, position);
            }
        }
    }

    private static void AddScalarRun8To13CountOnly(int runLength, ref long count)
    {
        if (runLength < 8)
        {
            return;
        }

        count += runLength / 13;
        if (runLength % 13 >= 8)
        {
            count++;
        }
    }

    private static int ConsumeFastCyrillicLetterPairs(ReadOnlySpan<byte> haystack, ref int position)
    {
        ref byte reference = ref MemoryMarshal.GetReference(haystack);
        int length = haystack.Length;
        int scalars = 0;
        while (position <= length - 8)
        {
            ulong chunk = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref reference, position));
            if (!IsFastCyrillicLetterChunk(chunk))
            {
                break;
            }

            position += 8;
            scalars += 4;
        }

        while (position + 1 < length &&
            IsFastCyrillicLetterPair(
                Unsafe.Add(ref reference, position),
                Unsafe.Add(ref reference, position + 1)))
        {
            position += 2;
            scalars++;
        }

        return scalars;
    }

    private static bool IsFastCyrillicLetterChunk(ulong chunk)
    {
        const ulong LeadByteMask = 0x00FF00FF00FF00FFUL;
        const ulong LeadByteValueMask = 0x00FE00FE00FE00FEUL;
        const ulong LeadByteExpected = 0x00D000D000D000D0UL;
        const ulong ContinuationValueMask = 0xC000C000C000C000UL;
        const ulong ContinuationExpected = 0x8000800080008000UL;

        return (chunk & LeadByteMask & LeadByteValueMask) == LeadByteExpected &&
            (chunk & ContinuationValueMask) == ContinuationExpected;
    }

    private static bool IsFastCyrillicLetterPair(byte first, byte second)
    {
        return (first == 0xD0 || first == 0xD1) &&
            second is >= 0x80 and <= 0xBF;
    }

    private bool TryUnicodeLetterFastPathMatchLength(ReadOnlySpan<byte> haystack, int position, out int scalarLength)
    {
        byte first = haystack[position];
        if (first <= 0x7F)
        {
            scalarLength = RegexSimpleSequenceSegment.IsAsciiLetter(first) ? 1 : 0;
            return scalarLength != 0;
        }

        if ((first == 0xD0 || first == 0xD1) &&
            position + 1 < haystack.Length &&
            haystack[position + 1] is >= 0x80 and <= 0xBF)
        {
            scalarLength = 2;
            return true;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            atoms[0].Kind,
            atoms[0].Value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out scalarLength);
    }

    private void AddScalarRunCountOnly(int runLength, ref long count)
    {
        if (runLength < minimum)
        {
            return;
        }

        int matchLength = lazy ? minimum : maximum;
        int fullMatches = runLength / matchLength;
        int remainder = runLength - fullMatches * matchLength;
        count += fullMatches;
        if (remainder >= minimum)
        {
            count++;
        }
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        length = 0;
        if (start < 0 || start >= haystack.Length)
        {
            return false;
        }

        int position = start;
        int count = 0;
        int selectedEnd = -1;
        while (count < maximum &&
            TryAtomMatchLength(haystack, position, out int scalarLength))
        {
            count++;
            position += scalarLength;
            if (count == minimum)
            {
                selectedEnd = position;
                if (lazy)
                {
                    break;
                }
            }
        }

        if (count < minimum)
        {
            return false;
        }

        length = (lazy ? selectedEnd : position) - start;
        return true;
    }

    private bool TryAtomMatchLength(ReadOnlySpan<byte> haystack, int position, out int scalarLength)
    {
        for (int index = 0; index < atoms.Length; index++)
        {
            RegexScalarRunAtom atom = atoms[index];
            if (atom.UnicodeLetterFastPath &&
                TryFastUnicodeLetterMatchLength(haystack, position, out bool matched, out scalarLength))
            {
                if (matched)
                {
                    return true;
                }

                continue;
            }

            if (RegexByteClass.TryGetAtomMatchLength(
                haystack,
                position,
                atom.Kind,
                atom.Value,
                options.CaseInsensitive,
                options.MultiLine,
                options.DotMatchesNewline,
                options.Crlf,
                options.LineTerminator,
                options.Utf8,
                options.UnicodeClasses,
                out scalarLength))
            {
                return true;
            }
        }

        scalarLength = 0;
        return false;
    }

    private static bool TryFastUnicodeLetterMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        out bool matched,
        out int scalarLength)
    {
        matched = false;
        scalarLength = 0;
        if (position >= haystack.Length)
        {
            return true;
        }

        byte first = haystack[position];
        if (first <= 0x7F)
        {
            matched = RegexSimpleSequenceSegment.IsAsciiLetter(first);
            scalarLength = matched ? 1 : 0;
            return true;
        }

        if ((first == 0xD0 || first == 0xD1) &&
            position + 1 < haystack.Length &&
            haystack[position + 1] is >= 0x80 and <= 0xBF)
        {
            matched = true;
            scalarLength = 2;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryUnicodeLowerOrUpperFastPathMatchLength(
        ReadOnlySpan<byte> haystack,
        int position,
        ref RegexUnicodeRangeCursor ranges,
        out int scalarLength)
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

        if (UnicodeLowerOrUpperFirstBytes[first] == 0)
        {
            if (!TryGetUtf8ScalarLength(haystack, position, out scalarLength))
            {
                scalarLength = 0;
            }

            return false;
        }

        if (!TryDecodeUtf8Scalar(haystack, position, out int scalar, out scalarLength))
        {
            scalarLength = 0;
            return false;
        }

        return ranges.Contains(scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDecodeUtf8Scalar(ReadOnlySpan<byte> haystack, int position, out int scalar, out int length)
    {
        scalar = 0;
        length = 0;
        byte first = haystack[position];
        if (first <= 0x7F)
        {
            scalar = first;
            length = 1;
            return true;
        }

        if ((first & 0xE0) == 0xC0)
        {
            if (position + 1 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]))
            {
                return false;
            }

            scalar = ((first & 0x1F) << 6) | (haystack[position + 1] & 0x3F);
            if (scalar < 0x80)
            {
                return false;
            }

            length = 2;
            return true;
        }

        if ((first & 0xF0) == 0xE0)
        {
            if (position + 2 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]))
            {
                return false;
            }

            scalar = ((first & 0x0F) << 12) |
                ((haystack[position + 1] & 0x3F) << 6) |
                (haystack[position + 2] & 0x3F);
            if (scalar < 0x800 || scalar is >= 0xD800 and <= 0xDFFF)
            {
                return false;
            }

            length = 3;
            return true;
        }

        if ((first & 0xF8) == 0xF0)
        {
            if (position + 3 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]) ||
                !IsUtf8Continuation(haystack[position + 3]))
            {
                return false;
            }

            scalar = ((first & 0x07) << 18) |
                ((haystack[position + 1] & 0x3F) << 12) |
                ((haystack[position + 2] & 0x3F) << 6) |
                (haystack[position + 3] & 0x3F);
            if (scalar < 0x10000 || scalar > 0x10FFFF)
            {
                return false;
            }

            length = 4;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetUtf8ScalarLength(ReadOnlySpan<byte> haystack, int position, out int length)
    {
        length = 0;
        byte first = haystack[position];
        if (first <= 0x7F)
        {
            length = 1;
            return true;
        }

        if (first is >= 0xC2 and <= 0xDF)
        {
            if (position + 1 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]))
            {
                return false;
            }

            length = 2;
            return true;
        }

        if (first is >= 0xE0 and <= 0xEF)
        {
            if (position + 2 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]) ||
                first == 0xE0 && haystack[position + 1] < 0xA0 ||
                first == 0xED && haystack[position + 1] >= 0xA0)
            {
                return false;
            }

            length = 3;
            return true;
        }

        if (first is >= 0xF0 and <= 0xF4)
        {
            if (position + 3 >= haystack.Length ||
                !IsUtf8Continuation(haystack[position + 1]) ||
                !IsUtf8Continuation(haystack[position + 2]) ||
                !IsUtf8Continuation(haystack[position + 3]) ||
                first == 0xF0 && haystack[position + 1] < 0x90 ||
                first == 0xF4 && haystack[position + 1] >= 0x90)
            {
                return false;
            }

            length = 4;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUtf8Continuation(byte value)
    {
        return (value & 0xC0) == 0x80;
    }

    private void AddTrailingRun(
        int runScalars,
        int runBytes,
        int currentScalars,
        int currentBytes,
        ref long count,
        ref long spanSum)
    {
        if (runScalars < minimum || currentScalars < minimum)
        {
            return;
        }

        count++;
        spanSum += lazy ? currentBytes : runBytes - BytesBeforeCurrentGreedyMatch(runScalars, currentScalars, runBytes, currentBytes);
    }

    private int BytesBeforeCurrentGreedyMatch(int runScalars, int currentScalars, int runBytes, int currentBytes)
    {
        if (lazy || currentScalars == runScalars)
        {
            return 0;
        }

        return runBytes - currentBytes;
    }

    private static int AdvanceAfterNonMatch(ReadOnlySpan<byte> haystack, int position)
    {
        if (haystack[position] <= 0x7F)
        {
            return position + 1;
        }

        return RegexByteClass.IsUtf8Boundary(haystack, position) &&
            Rune.DecodeFromUtf8(haystack[position..], out _, out int length) == OperationStatus.Done
            ? position + length
            : position + 1;
    }

    private static bool TryExtractScalarRunNode(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        out RegexSyntaxNode? scalarRunNode,
        out RegexCompileOptions effectiveOptions)
    {
        scalarRunNode = null;
        effectiveOptions = options;
        root = UnwrapTransparentGroups(root);
        if (root is not RegexSequenceNode sequence)
        {
            scalarRunNode = root;
            return true;
        }

        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode node = sequence.Nodes[index];
            if (node is RegexInlineFlagsNode flags)
            {
                effectiveOptions = effectiveOptions.Apply(flags.EnabledFlags, flags.DisabledFlags);
                continue;
            }

            if (scalarRunNode is not null)
            {
                return false;
            }

            scalarRunNode = node;
        }

        return scalarRunNode is not null;
    }

    private static bool TryCollectScalarAtoms(
        RegexSyntaxNode node,
        RegexCompileOptions options,
        out RegexScalarRunAtom[] atoms)
    {
        node = UnwrapTransparentGroups(node);
        if (node is RegexAtomNode atom)
        {
            return TryCreateScalarAtom(atom, options, out _, out atoms);
        }

        if (node is not RegexAlternationNode alternation || alternation.Alternatives.Count == 0)
        {
            atoms = [];
            return false;
        }

        var collected = new List<RegexScalarRunAtom>(alternation.Alternatives.Count);
        for (int index = 0; index < alternation.Alternatives.Count; index++)
        {
            if (UnwrapTransparentGroups(alternation.Alternatives[index]) is not RegexAtomNode alternative ||
                !TryCreateScalarAtom(alternative, options, out RegexScalarRunAtom scalarAtom, out _))
            {
                atoms = [];
                return false;
            }

            collected.Add(scalarAtom);
        }

        atoms = [.. collected];
        return true;
    }

    internal static bool TryCreateScalarAtom(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out RegexScalarRunAtom scalarAtom,
        out RegexScalarRunAtom[] atoms)
    {
        scalarAtom = default;
        atoms = [];
        if (!RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        bool unicodeLetterFastPath = atom.Kind == RegexSyntaxKind.UnicodePropertyClass &&
            atom.Value.Length == 1 &&
            atom.Value.Span[0] == (byte)RegexUnicodePropertyKind.Letter &&
            options.UnicodeClasses &&
            !options.CaseInsensitive;
        scalarAtom = new RegexScalarRunAtom(atom.Kind, atom.Value.ToArray(), unicodeLetterFastPath);
        atoms = [scalarAtom];
        return true;
    }

    private static bool IsUnicodeLowerOrUpperFastPath(RegexScalarRunAtom[] atoms, RegexCompileOptions options)
    {
        if (!options.UnicodeClasses ||
            options.CaseInsensitive ||
            atoms.Length != 2)
        {
            return false;
        }

        bool hasLowercase = false;
        bool hasUppercase = false;
        for (int index = 0; index < atoms.Length; index++)
        {
            RegexScalarRunAtom atom = atoms[index];
            if (atom.Kind != RegexSyntaxKind.UnicodePropertyClass ||
                atom.Value.Length != 1)
            {
                return false;
            }

            var kind = (RegexUnicodePropertyKind)atom.Value[0];
            hasLowercase |= kind == RegexUnicodePropertyKind.Lowercase;
            hasUppercase |= kind == RegexUnicodePropertyKind.Uppercase;
        }

        return hasLowercase && hasUppercase;
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
