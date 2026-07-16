using System.Text;

namespace Scout;

/// <summary>
/// Holds the parsed literal and capture references in a replacement expression.
/// </summary>
/// <param name="parts">The parsed replacement parts.</param>
/// <param name="highestCapture">The highest capture index needed by the expression.</param>
/// <param name="usesNamedCaptureReferences">Whether the expression contains a named reference.</param>
/// <param name="literalLength">The total length of literal replacement bytes.</param>
internal sealed class ReplacementTemplate(
    ReplacementTemplatePart[] parts,
    int highestCapture,
    bool usesNamedCaptureReferences,
    int literalLength)
{
    private readonly ReplacementTemplatePart[] _parts = parts;

    internal int HighestCapture { get; } = highestCapture;

    internal bool UsesNamedCaptureReferences { get; } = usesNamedCaptureReferences;

    internal int LiteralLength { get; } = literalLength;

    internal bool RequiresSubcaptures => HighestCapture > 0 || UsesNamedCaptureReferences;

    internal static ReplacementTemplate Create(ReadOnlySpan<byte> replacement, int captureCount = 0)
    {
        List<ReplacementTemplatePart> parts = [];
        List<byte> literal = [];
        int highestCapture = captureCount;
        bool usesNamedCaptureReferences = false;
        int literalLength = 0;

        for (int index = 0; index < replacement.Length; index++)
        {
            byte value = replacement[index];
            if (value != (byte)'$')
            {
                literal.Add(value);
                literalLength++;
                continue;
            }

            if (index + 1 >= replacement.Length)
            {
                literal.Add(value);
                literalLength++;
                continue;
            }

            byte next = replacement[index + 1];
            if (next == (byte)'$')
            {
                literal.Add((byte)'$');
                literalLength++;
                index++;
                continue;
            }

            if (next == (byte)'{')
            {
                int close = replacement[(index + 2)..].IndexOf((byte)'}');
                if (close < 0)
                {
                    literal.Add(value);
                    literalLength++;
                    continue;
                }

                ReadOnlySpan<byte> capture = replacement.Slice(index + 2, close);
                if (capture.Length == 0)
                {
                    AppendLiteral(parts, literal);
                    AddLiteral(parts, replacement.Slice(index, close + 3).ToArray());
                    literalLength += close + 3;
                }
                else if (TryReadCaptureIndex(capture, out int captureIndex))
                {
                    AppendLiteral(parts, literal);
                    parts.Add(new ReplacementTemplatePart(Literal: null, captureIndex, CaptureName: null));
                    highestCapture = Math.Max(highestCapture, captureIndex);
                }
                else if (IsCaptureName(capture))
                {
                    AppendLiteral(parts, literal);
                    parts.Add(new ReplacementTemplatePart(Literal: null, CaptureIndex: -1, Encoding.ASCII.GetString(capture)));
                    usesNamedCaptureReferences = true;
                }

                index += close + 2;
                continue;
            }

            if (IsAsciiDigit(next))
            {
                int end = index + 1;
                while (end < replacement.Length && IsAsciiDigit(replacement[end]))
                {
                    end++;
                }

                if (TryReadCaptureIndex(replacement[(index + 1)..end], out int captureIndex))
                {
                    AppendLiteral(parts, literal);
                    parts.Add(new ReplacementTemplatePart(Literal: null, captureIndex, CaptureName: null));
                    highestCapture = Math.Max(highestCapture, captureIndex);
                }

                index = end - 1;
                continue;
            }

            if (IsCaptureNameByte(next))
            {
                int end = index + 1;
                while (end < replacement.Length && IsCaptureNameByte(replacement[end]))
                {
                    end++;
                }

                AppendLiteral(parts, literal);
                parts.Add(new ReplacementTemplatePart(
                    Literal: null,
                    CaptureIndex: -1,
                    Encoding.ASCII.GetString(replacement[(index + 1)..end])));
                usesNamedCaptureReferences = true;
                index = end - 1;
                continue;
            }

            literal.Add(value);
            literalLength++;
        }

        AppendLiteral(parts, literal);
        return new ReplacementTemplate(parts.ToArray(), highestCapture, usesNamedCaptureReferences, literalLength);
    }

    internal void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<int> captureSlots,
        IReadOnlyDictionary<string, int>? captureNames)
    {
        for (int index = 0; index < _parts.Length; index++)
        {
            ReplacementTemplatePart part = _parts[index];
            if (part.IsLiteral)
            {
                Add(bytes, part.Literal);
            }
            else if (part.CaptureIndex >= 0)
            {
                AddCapture(bytes, haystack, captureSlots, part.CaptureIndex);
            }
            else if (part.CaptureName is not null &&
                captureNames is not null &&
                captureNames.TryGetValue(part.CaptureName, out int captureIndex))
            {
                AddCapture(bytes, haystack, captureSlots, captureIndex);
            }
        }
    }

    /// <summary>
    /// Writes the expanded replacement directly from caller-owned capture slots.
    /// </summary>
    /// <param name="output">The destination byte writer.</param>
    /// <param name="haystack">The complete haystack containing the capture spans.</param>
    /// <param name="captureSlots">The absolute start and exclusive-end offsets for every capture.</param>
    /// <param name="captureNames">The immutable capture indexes keyed by name.</param>
    internal void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<int> captureSlots,
        IReadOnlyDictionary<string, int>? captureNames)
    {
        for (int index = 0; index < _parts.Length; index++)
        {
            ReplacementTemplatePart part = _parts[index];
            if (part.IsLiteral)
            {
                output.Write(part.Literal);
            }
            else if (part.CaptureIndex >= 0)
            {
                WriteCapture(output, haystack, captureSlots, part.CaptureIndex);
            }
            else if (part.CaptureName is not null &&
                captureNames is not null &&
                captureNames.TryGetValue(part.CaptureName, out int captureIndex))
            {
                WriteCapture(output, haystack, captureSlots, captureIndex);
            }
        }
    }

    private static void AppendLiteral(List<ReplacementTemplatePart> parts, List<byte> literal)
    {
        if (literal.Count == 0)
        {
            return;
        }

        AddLiteral(parts, literal.ToArray());
        literal.Clear();
    }

    private static void AddLiteral(List<ReplacementTemplatePart> parts, byte[] literal)
    {
        parts.Add(new ReplacementTemplatePart(literal, CaptureIndex: -1, CaptureName: null));
    }

    private static bool TryReadCaptureIndex(ReadOnlySpan<byte> capture, out int captureIndex)
    {
        captureIndex = 0;
        if (capture.IsEmpty)
        {
            return false;
        }

        for (int index = 0; index < capture.Length; index++)
        {
            if (!IsAsciiDigit(capture[index]))
            {
                return false;
            }

            int digit = capture[index] - (byte)'0';
            if (captureIndex > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            captureIndex = (captureIndex * 10) + digit;
        }

        return true;
    }

    private static void AddCapture(
        List<byte> bytes,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<int> captureSlots,
        int captureIndex)
    {
        if (!TryGetCaptureRange(
            haystack.Length,
            captureSlots,
            captureIndex,
            out int start,
            out int length))
        {
            return;
        }

        Add(bytes, haystack.Slice(start, length));
    }

    private static void WriteCapture(
        RawByteWriter output,
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<int> captureSlots,
        int captureIndex)
    {
        if (!TryGetCaptureRange(
            haystack.Length,
            captureSlots,
            captureIndex,
            out int start,
            out int length))
        {
            return;
        }

        output.Write(haystack.Slice(start, length));
    }

    private static bool TryGetCaptureRange(
        int haystackLength,
        ReadOnlySpan<int> captureSlots,
        int captureIndex,
        out int start,
        out int length)
    {
        if ((uint)captureIndex >= (uint)(captureSlots.Length / 2))
        {
            start = -1;
            length = 0;
            return false;
        }

        int slot = 2 * captureIndex;
        start = captureSlots[slot];
        int end = captureSlots[slot + 1];
        if (start < 0 || end < start || end > haystackLength)
        {
            length = 0;
            return false;
        }

        length = end - start;
        return true;
    }

    private static bool IsCaptureName(ReadOnlySpan<byte> capture)
    {
        if (capture.IsEmpty || !IsCaptureNameStartByte(capture[0]))
        {
            return false;
        }

        for (int index = 1; index < capture.Length; index++)
        {
            if (!IsCaptureNameByte(capture[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiDigit(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9';
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

    private static void Add(List<byte> bytes, ReadOnlySpan<byte> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            bytes.Add(values[index]);
        }
    }
}
