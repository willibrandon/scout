using System.Buffers;
using System.Text;

namespace Scout;

internal sealed class RegexScalarRunEngine
{
    private const int MaxBoundedRepeat = 1024;

    private readonly RegexSyntaxKind kind;
    private readonly byte[] value;
    private readonly RegexCompileOptions options;
    private readonly int minimum;
    private readonly int maximum;
    private readonly bool lazy;
    private readonly bool unicodeLetterFastPath;

    private RegexScalarRunEngine(RegexAtomNode atom, RegexCompileOptions options, int minimum, int maximum, bool lazy)
    {
        kind = atom.Kind;
        value = atom.Value.ToArray();
        this.options = options;
        this.minimum = minimum;
        this.maximum = maximum;
        this.lazy = lazy;
        unicodeLetterFastPath = atom.Kind == RegexSyntaxKind.UnicodePropertyClass &&
            atom.Value.Length == 1 &&
            atom.Value.Span[0] == (byte)RegexUnicodePropertyKind.Letter &&
            options.UnicodeClasses &&
            !options.CaseInsensitive;
    }

    public static bool TryCreate(RegexSyntaxNode root, RegexCompileOptions options, out RegexScalarRunEngine? engine)
    {
        engine = null;
        root = UnwrapTransparentGroups(root);
        if (!options.UnicodeClasses ||
            root is not RegexRepetitionNode { Minimum: > 0, Maximum: { } maximum } repetition ||
            maximum > MaxBoundedRepeat ||
            UnwrapTransparentGroups(repetition.Child) is not RegexAtomNode atom ||
            !RegexByteClass.RequiresUtf8ScalarMatch(atom.Kind, atom.Value.Span, options.Utf8, options.CaseInsensitive, options.UnicodeClasses))
        {
            return false;
        }

        engine = new RegexScalarRunEngine(atom, options, repetition.Minimum, maximum, repetition.Lazy);
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

    public bool TryCountNonOverlapping(ReadOnlySpan<byte> haystack, int startAt, out long count, out long spanSum)
    {
        count = 0;
        spanSum = 0;
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
        if (unicodeLetterFastPath &&
            TryFastUnicodeLetterMatchLength(haystack, position, out bool matched, out scalarLength))
        {
            return matched;
        }

        return RegexByteClass.TryGetAtomMatchLength(
            haystack,
            position,
            kind,
            value,
            options.CaseInsensitive,
            options.MultiLine,
            options.DotMatchesNewline,
            options.Crlf,
            options.LineTerminator,
            options.Utf8,
            options.UnicodeClasses,
            out scalarLength);
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
