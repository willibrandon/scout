using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexScalarRunEngine
{
    private const int MaxBoundedRepeat = 1024;

    private readonly RegexScalarRunAtom[] atoms;
    private readonly RegexCompileOptions options;
    private readonly int minimum;
    private readonly int maximum;
    private readonly bool lazy;

    private RegexScalarRunEngine(RegexScalarRunAtom[] atoms, RegexCompileOptions options, int minimum, int maximum, bool lazy)
    {
        this.atoms = atoms;
        this.options = options;
        this.minimum = minimum;
        this.maximum = maximum;
        this.lazy = lazy;
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

        engine = new RegexScalarRunEngine(atoms, effectiveOptions, repetition.Minimum, maximum, repetition.Lazy);
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
