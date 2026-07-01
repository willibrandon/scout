using System.Text;

namespace Scout;

internal sealed class ReplacementTemplate
{
    private readonly ReplacementTemplatePart[] parts;

    private ReplacementTemplate(
        ReplacementTemplatePart[] parts,
        int highestCapture,
        bool usesNamedCaptureReferences,
        int literalLength)
    {
        this.parts = parts;
        HighestCapture = highestCapture;
        UsesNamedCaptureReferences = usesNamedCaptureReferences;
        LiteralLength = literalLength;
    }

    public int HighestCapture { get; }

    public bool UsesNamedCaptureReferences { get; }

    public int LiteralLength { get; }

    public static ReplacementTemplate Create(ReadOnlySpan<byte> replacement, IReadOnlyList<byte[]> patterns)
    {
        List<ReplacementTemplatePart> parts = [];
        List<byte> literal = [];
        int highestCapture = CountHighestCaptureIndex(patterns);
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

    public void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int>? captureNames)
    {
        for (int index = 0; index < parts.Length; index++)
        {
            ReplacementTemplatePart part = parts[index];
            if (part.IsLiteral)
            {
                Add(bytes, part.Literal);
            }
            else if (part.CaptureIndex >= 0)
            {
                AddCapture(bytes, matched, captureStarts, captureLengths, part.CaptureIndex);
            }
            else if (part.CaptureName is not null &&
                captureNames is not null &&
                captureNames.TryGetValue(part.CaptureName, out int captureIndex))
            {
                AddCapture(bytes, matched, captureStarts, captureLengths, captureIndex);
            }
        }
    }

    public void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> matched,
        int firstCaptureIndex,
        int firstCaptureStart,
        int firstCaptureLength,
        int secondCaptureIndex,
        int secondCaptureStart,
        int secondCaptureLength)
    {
        for (int index = 0; index < parts.Length; index++)
        {
            ReplacementTemplatePart part = parts[index];
            if (part.IsLiteral)
            {
                Add(bytes, part.Literal);
            }
            else if (part.CaptureIndex == 0)
            {
                Add(bytes, matched);
            }
            else if (part.CaptureIndex == firstCaptureIndex)
            {
                Add(bytes, matched.Slice(firstCaptureStart, firstCaptureLength));
            }
            else if (part.CaptureIndex == secondCaptureIndex)
            {
                Add(bytes, matched.Slice(secondCaptureStart, secondCaptureLength));
            }
        }
    }

    public void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> matched,
        int firstCaptureIndex,
        int firstCaptureStart,
        int firstCaptureLength,
        int secondCaptureIndex,
        int secondCaptureStart,
        int secondCaptureLength)
    {
        for (int index = 0; index < parts.Length; index++)
        {
            ReplacementTemplatePart part = parts[index];
            if (part.IsLiteral)
            {
                output.Write(part.Literal);
            }
            else if (part.CaptureIndex == 0)
            {
                output.Write(matched);
            }
            else if (part.CaptureIndex == firstCaptureIndex)
            {
                output.Write(matched.Slice(firstCaptureStart, firstCaptureLength));
            }
            else if (part.CaptureIndex == secondCaptureIndex)
            {
                output.Write(matched.Slice(secondCaptureStart, secondCaptureLength));
            }
        }
    }

    private static int CountHighestCaptureIndex(IReadOnlyList<byte[]> patterns)
    {
        int highest = 0;
        for (int index = 0; index < patterns.Count; index++)
        {
            highest = Math.Max(highest, ReplacementFormatter.CountCapturingGroups(patterns[index]));
        }

        return highest;
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
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths,
        int captureIndex)
    {
        if ((uint)captureIndex >= (uint)captureStarts.Length)
        {
            return;
        }

        int start = captureStarts[captureIndex];
        int length = captureLengths[captureIndex];
        if (start < 0 || length < 0 || start + length > matched.Length)
        {
            return;
        }

        Add(bytes, matched.Slice(start, length));
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
