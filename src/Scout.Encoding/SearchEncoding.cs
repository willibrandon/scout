
namespace Scout;

/// <summary>
/// Transcodes search input bytes into Scout's internal UTF-8 byte representation.
/// </summary>
public static class SearchEncoding
{
    /// <summary>
    /// Decodes search bytes according to the requested encoding mode.
    /// </summary>
    /// <param name="bytes">The source bytes.</param>
    /// <param name="encodingKind">The requested encoding mode.</param>
    /// <returns>The original bytes for raw modes, or decoded UTF-8 bytes for transcoding modes.</returns>
    public static byte[] Decode(byte[] bytes, SearchEncodingKind encodingKind)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (encodingKind == SearchEncodingKind.None || (encodingKind == SearchEncodingKind.Auto && !HasBom(bytes)))
        {
            return bytes;
        }

        return Decode(bytes.AsSpan(), encodingKind);
    }

    /// <summary>
    /// Decodes search bytes according to the requested encoding mode.
    /// </summary>
    /// <param name="bytes">The source bytes.</param>
    /// <param name="encodingKind">The requested encoding mode.</param>
    /// <returns>Decoded UTF-8 bytes.</returns>
    public static byte[] Decode(ReadOnlySpan<byte> bytes, SearchEncodingKind encodingKind)
    {
        if (encodingKind == SearchEncodingKind.None)
        {
            return bytes.ToArray();
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
            return encodingKind == SearchEncodingKind.Utf8
                ? DecodeUtf8(bytes)
                : bytes.ToArray();
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return DecodeUtf16(bytes[2..], bigEndian: false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return DecodeUtf16(bytes[2..], bigEndian: true);
        }

        return DecodeWithoutBom(bytes, encodingKind);
    }

    internal static byte[] DecodeWithoutBom(ReadOnlySpan<byte> bytes, SearchEncodingKind encodingKind)
    {
        return encodingKind switch
        {
            SearchEncodingKind.None or SearchEncodingKind.Auto => bytes.ToArray(),
            SearchEncodingKind.Utf8 => DecodeUtf8(bytes),
            SearchEncodingKind.Utf16 or SearchEncodingKind.Utf16Le => DecodeUtf16(bytes, bigEndian: false),
            SearchEncodingKind.Utf16Be => DecodeUtf16(bytes, bigEndian: true),
            SearchEncodingKind.EucKr => DecodeEucKr(bytes),
            SearchEncodingKind.EucJp => DecodeEucJp(bytes),
            SearchEncodingKind.Big5 => DecodeBig5(bytes),
            SearchEncodingKind.Gb18030 or SearchEncodingKind.Gbk => DecodeGb18030(bytes),
            SearchEncodingKind.ShiftJis => DecodeShiftJis(bytes),
            SearchEncodingKind.Ibm866 => DecodeSingleByte(bytes, SearchSingleByteTables.Ibm866),
            SearchEncodingKind.Iso88592 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88592),
            SearchEncodingKind.Iso88593 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88593),
            SearchEncodingKind.Iso88594 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88594),
            SearchEncodingKind.Iso88595 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88595),
            SearchEncodingKind.Iso88596 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88596),
            SearchEncodingKind.Iso88597 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88597),
            SearchEncodingKind.Iso88598 or SearchEncodingKind.Iso88598I => DecodeSingleByte(bytes, SearchSingleByteTables.Iso88598),
            SearchEncodingKind.Iso885910 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso885910),
            SearchEncodingKind.Iso885913 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso885913),
            SearchEncodingKind.Iso885914 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso885914),
            SearchEncodingKind.Iso885915 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso885915),
            SearchEncodingKind.Iso885916 => DecodeSingleByte(bytes, SearchSingleByteTables.Iso885916),
            SearchEncodingKind.Iso2022Jp => DecodeIso2022Jp(bytes),
            SearchEncodingKind.Koi8R => DecodeSingleByte(bytes, SearchSingleByteTables.Koi8R),
            SearchEncodingKind.Koi8U => DecodeSingleByte(bytes, SearchSingleByteTables.Koi8U),
            SearchEncodingKind.Macintosh => DecodeSingleByte(bytes, SearchSingleByteTables.Macintosh),
            SearchEncodingKind.Windows874 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows874),
            SearchEncodingKind.Windows1250 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1250),
            SearchEncodingKind.Windows1251 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1251),
            SearchEncodingKind.Windows1252 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1252),
            SearchEncodingKind.Windows1253 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1253),
            SearchEncodingKind.Windows1254 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1254),
            SearchEncodingKind.Windows1255 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1255),
            SearchEncodingKind.Windows1256 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1256),
            SearchEncodingKind.Windows1257 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1257),
            SearchEncodingKind.Windows1258 => DecodeSingleByte(bytes, SearchSingleByteTables.Windows1258),
            SearchEncodingKind.XMacCyrillic => DecodeSingleByte(bytes, SearchSingleByteTables.XMacCyrillic),
            SearchEncodingKind.XUserDefined => DecodeUserDefined(bytes),
            _ => bytes.ToArray(),
        };
    }

    private static bool HasBom(ReadOnlySpan<byte> bytes)
    {
        return (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
            (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));
    }

    internal static int GetStreamingSafePrefixLength(ReadOnlySpan<byte> bytes, SearchEncodingKind encodingKind)
    {
        return encodingKind switch
        {
            SearchEncodingKind.Utf8 => GetUtf8SafePrefixLength(bytes),
            SearchEncodingKind.Utf16 or SearchEncodingKind.Utf16Le => GetUtf16SafePrefixLength(bytes, bigEndian: false),
            SearchEncodingKind.Utf16Be => GetUtf16SafePrefixLength(bytes, bigEndian: true),
            SearchEncodingKind.EucKr => GetEucKrSafePrefixLength(bytes),
            SearchEncodingKind.EucJp => GetEucJpSafePrefixLength(bytes),
            SearchEncodingKind.Big5 => GetBig5SafePrefixLength(bytes),
            SearchEncodingKind.Gb18030 or SearchEncodingKind.Gbk => GetGb18030SafePrefixLength(bytes),
            SearchEncodingKind.ShiftJis => GetShiftJisSafePrefixLength(bytes),
            _ => bytes.Length,
        };
    }

    internal static byte[] DecodeIso2022JpSegment(
        ReadOnlySpan<byte> bytes,
        ref Iso2022JpDecoderState decoderState,
        bool flush)
    {
        return DecodeIso2022Jp(bytes, ref decoderState, flush);
    }

    private static int GetUtf8SafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            int sequenceLength = GetUtf8SequenceLength(first);
            if (sequenceLength == 0)
            {
                index++;
                continue;
            }

            if (index + sequenceLength > bytes.Length)
            {
                break;
            }

            if (TryDecodeUtf8Scalar(bytes[index..], out _, out int decodedLength))
            {
                index += decodedLength;
                continue;
            }

            index++;
        }

        return index;
    }

    private static int GetUtf8SequenceLength(byte first)
    {
        if (first <= 0x7F)
        {
            return 1;
        }

        if (first is >= 0xC2 and <= 0xDF)
        {
            return 2;
        }

        if (first is >= 0xE0 and <= 0xEF)
        {
            return 3;
        }

        return first is >= 0xF0 and <= 0xF4 ? 4 : 0;
    }

    private static int GetUtf16SafePrefixLength(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            if (index + 1 >= bytes.Length)
            {
                break;
            }

            int unitStart = index;
            ushort unit = ReadUtf16Unit(bytes[index], bytes[index + 1], bigEndian);
            index += 2;
            if (unit is not (>= 0xD800 and <= 0xDBFF))
            {
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                index = unitStart;
                break;
            }

            ushort low = ReadUtf16Unit(bytes[index], bytes[index + 1], bigEndian);
            if (low is >= 0xDC00 and <= 0xDFFF)
            {
                index += 2;
            }
        }

        return index;
    }

    private static int GetEucKrSafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                index++;
                continue;
            }

            int leadMinusOffset = first - 0x81;
            if (leadMinusOffset is < 0 or > 0x7D)
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                break;
            }

            byte second = bytes[index + 1];
            index += TryDecodeEucKrPair(leadMinusOffset, second, out _) || second >= 0x80 ? 2 : 1;
        }

        return index;
    }

    private static int GetEucJpSafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                index++;
                continue;
            }

            if (first == 0x8E)
            {
                if (index + 1 >= bytes.Length)
                {
                    break;
                }

                byte second = bytes[index + 1];
                int trailMinusOffset = second - 0xA1;
                index += trailMinusOffset is >= 0 and <= 0x3E || second >= 0x80 ? 2 : 1;
                continue;
            }

            if (first == 0x8F)
            {
                if (index + 1 >= bytes.Length)
                {
                    break;
                }

                byte lead = bytes[index + 1];
                int leadMinusOffset = lead - 0xA1;
                if (leadMinusOffset is < 0 or > 0x5D)
                {
                    index += lead < 0x80 ? 1 : 2;
                    continue;
                }

                if (index + 2 >= bytes.Length)
                {
                    break;
                }

                byte trail = bytes[index + 2];
                int trailMinusOffset = trail - 0xA1;
                index += trailMinusOffset is < 0 or > 0x5D
                    ? trail < 0x80 ? 2 : 3
                    : 3;
                continue;
            }

            int firstMinusOffset = first - 0xA1;
            if (firstMinusOffset is < 0 or > 0x5D)
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                break;
            }

            byte secondByte = bytes[index + 1];
            int secondMinusOffset = secondByte - 0xA1;
            index += secondMinusOffset is < 0 or > 0x5D
                ? secondByte < 0x80 ? 1 : 2
                : 2;
        }

        return index;
    }

    private static int GetBig5SafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                index++;
                continue;
            }

            int leadMinusOffset = first - 0x81;
            if (leadMinusOffset is < 0 or > 0x7D)
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                break;
            }

            byte second = bytes[index + 1];
            index += TryGetBig5Trail(second, out _) || second >= 0x80 ? 2 : 1;
        }

        return index;
    }

    private static int GetGb18030SafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                index++;
                continue;
            }

            int firstMinusOffset = first - 0x81;
            if (firstMinusOffset is < 0 or > 0x7D)
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                break;
            }

            byte second = bytes[index + 1];
            int secondMinusOffset = second - 0x30;
            if (secondMinusOffset is >= 0 and <= 9)
            {
                if (index + 2 >= bytes.Length)
                {
                    break;
                }

                byte third = bytes[index + 2];
                int thirdMinusOffset = third - 0x81;
                if (thirdMinusOffset is < 0 or > 0x7D)
                {
                    index += 3;
                    continue;
                }

                if (index + 3 >= bytes.Length)
                {
                    break;
                }

                index += 4;
                continue;
            }

            index += TryDecodeGb18030TwoByte(firstMinusOffset, second, out _) || second >= 0x80 ? 2 : 1;
        }

        return index;
    }

    private static int GetShiftJisSafePrefixLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F || !IsShiftJisLead(first))
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                break;
            }

            byte second = bytes[index + 1];
            index += TryGetShiftJisTrail(second, out _) || second >= 0x80 ? 2 : 1;
        }

        return index;
    }

    private static byte[] DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            if (TryDecodeUtf8Scalar(bytes[index..], out int scalar, out int length))
            {
                AppendUtf8Scalar(output, scalar);
                index += length;
                continue;
            }

            AppendUtf8Scalar(output, 0xFFFD);
            index++;
        }

        return output.ToArray();
    }

    private static bool TryDecodeUtf8Scalar(ReadOnlySpan<byte> bytes, out int scalar, out int length)
    {
        scalar = 0;
        length = 0;
        byte first = bytes[0];
        if (first is >= 0xC2 and <= 0xDF)
        {
            if (bytes.Length < 2 || !IsContinuation(bytes[1]))
            {
                return false;
            }

            scalar = ((first & 0x1F) << 6) | (bytes[1] & 0x3F);
            length = 2;
            return true;
        }

        if (first == 0xE0)
        {
            return TryDecodeThreeByteScalar(bytes, secondMin: 0xA0, secondMax: 0xBF, out scalar, out length);
        }

        if (first is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF)
        {
            return TryDecodeThreeByteScalar(bytes, secondMin: 0x80, secondMax: 0xBF, out scalar, out length);
        }

        if (first == 0xED)
        {
            return TryDecodeThreeByteScalar(bytes, secondMin: 0x80, secondMax: 0x9F, out scalar, out length);
        }

        if (first == 0xF0)
        {
            return TryDecodeFourByteScalar(bytes, secondMin: 0x90, secondMax: 0xBF, out scalar, out length);
        }

        if (first is >= 0xF1 and <= 0xF3)
        {
            return TryDecodeFourByteScalar(bytes, secondMin: 0x80, secondMax: 0xBF, out scalar, out length);
        }

        if (first == 0xF4)
        {
            return TryDecodeFourByteScalar(bytes, secondMin: 0x80, secondMax: 0x8F, out scalar, out length);
        }

        return false;
    }

    private static bool TryDecodeThreeByteScalar(
        ReadOnlySpan<byte> bytes,
        byte secondMin,
        byte secondMax,
        out int scalar,
        out int length)
    {
        scalar = 0;
        length = 0;
        if (bytes.Length < 3 ||
            bytes[1] < secondMin ||
            bytes[1] > secondMax ||
            !IsContinuation(bytes[2]))
        {
            return false;
        }

        scalar = ((bytes[0] & 0x0F) << 12) | ((bytes[1] & 0x3F) << 6) | (bytes[2] & 0x3F);
        length = 3;
        return true;
    }

    private static bool TryDecodeFourByteScalar(
        ReadOnlySpan<byte> bytes,
        byte secondMin,
        byte secondMax,
        out int scalar,
        out int length)
    {
        scalar = 0;
        length = 0;
        if (bytes.Length < 4 ||
            bytes[1] < secondMin ||
            bytes[1] > secondMax ||
            !IsContinuation(bytes[2]) ||
            !IsContinuation(bytes[3]))
        {
            return false;
        }

        scalar = ((bytes[0] & 0x07) << 18) | ((bytes[1] & 0x3F) << 12) | ((bytes[2] & 0x3F) << 6) | (bytes[3] & 0x3F);
        length = 4;
        return true;
    }

    private static byte[] DecodeUtf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            ushort unit = ReadUtf16Unit(bytes[index], bytes[index + 1], bigEndian);
            index += 2;
            if (unit is >= 0xD800 and <= 0xDBFF)
            {
                if (index + 1 < bytes.Length)
                {
                    ushort low = ReadUtf16Unit(bytes[index], bytes[index + 1], bigEndian);
                    if (low is >= 0xDC00 and <= 0xDFFF)
                    {
                        index += 2;
                        int scalar = 0x10000 + (((unit - 0xD800) << 10) | (low - 0xDC00));
                        AppendUtf8Scalar(output, scalar);
                        continue;
                    }
                }

                AppendUtf8Scalar(output, 0xFFFD);
                continue;
            }

            AppendUtf8Scalar(output, unit is >= 0xDC00 and <= 0xDFFF ? 0xFFFD : unit);
        }

        return output.ToArray();
    }

    private static ushort ReadUtf16Unit(byte first, byte second, bool bigEndian)
    {
        return bigEndian
            ? (ushort)((first << 8) | second)
            : (ushort)(first | (second << 8));
    }

    private static byte[] DecodeSingleByte(ReadOnlySpan<byte> bytes, ReadOnlySpan<ushort> table)
    {
        var output = new List<byte>(bytes.Length);
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            int scalar = value < 0x80 ? value : table[value - 0x80];
            AppendUtf8Scalar(output, scalar == 0 ? 0xFFFD : scalar);
        }

        return output.ToArray();
    }

    private static byte[] DecodeIso2022Jp(ReadOnlySpan<byte> bytes)
    {
        Iso2022JpDecoderState decoderState = default;
        return DecodeIso2022Jp(bytes, ref decoderState, flush: true);
    }

    private static byte[] DecodeIso2022Jp(
        ReadOnlySpan<byte> bytes,
        ref Iso2022JpDecoderState decoderState,
        bool flush)
    {
        var output = new List<byte>(bytes.Length);
        Iso2022JpState state = decoderState.State;
        Iso2022JpState outputState = decoderState.OutputState;
        byte lead = decoderState.Lead;
        bool outputFlag = decoderState.OutputFlag;
        int index = 0;
        while (index < bytes.Length)
        {
            byte value = bytes[index];
            switch (state)
            {
                case Iso2022JpState.Ascii:
                    DecodeIso2022JpAscii(value, output, ref state, ref outputFlag);
                    index++;
                    break;

                case Iso2022JpState.Roman:
                    DecodeIso2022JpRoman(value, output, ref state, ref outputFlag);
                    index++;
                    break;

                case Iso2022JpState.Katakana:
                    DecodeIso2022JpKatakana(value, output, ref state, ref outputFlag);
                    index++;
                    break;

                case Iso2022JpState.LeadByte:
                    DecodeIso2022JpLeadByte(value, output, ref state, ref lead, ref outputFlag);
                    index++;
                    break;

                case Iso2022JpState.TrailByte:
                    DecodeIso2022JpTrailByte(value, output, ref state, ref lead, ref outputFlag);
                    index++;
                    break;

                case Iso2022JpState.EscapeStart:
                    if (value is (byte)'$' or (byte)'(')
                    {
                        lead = value;
                        state = Iso2022JpState.Escape;
                        index++;
                    }
                    else
                    {
                        outputFlag = false;
                        state = outputState;
                        AppendUtf8Scalar(output, 0xFFFD);
                    }

                    break;

                case Iso2022JpState.Escape:
                    if (TryGetIso2022JpEscapeState(lead, value, out Iso2022JpState escapeState))
                    {
                        lead = 0;
                        state = escapeState;
                        outputState = escapeState;
                        bool previousOutputFlag = outputFlag;
                        outputFlag = true;
                        if (previousOutputFlag)
                        {
                            AppendUtf8Scalar(output, 0xFFFD);
                        }

                        index++;
                    }
                    else
                    {
                        byte prepended = lead;
                        outputFlag = false;
                        state = outputState;
                        lead = 0;
                        AppendUtf8Scalar(output, 0xFFFD);
                        PrependIso2022JpByte(prepended, output, ref state, ref lead);
                    }

                    break;
            }
        }

        if (flush)
        {
            FlushIso2022JpEnd(output, ref state, outputState, ref lead);
        }

        decoderState.State = state;
        decoderState.OutputState = outputState;
        decoderState.Lead = lead;
        decoderState.OutputFlag = outputFlag;
        return output.ToArray();
    }

    private static void DecodeIso2022JpAscii(
        byte value,
        List<byte> output,
        ref Iso2022JpState state,
        ref bool outputFlag)
    {
        if (value == 0x1B)
        {
            state = Iso2022JpState.EscapeStart;
            return;
        }

        outputFlag = false;
        if (value > 0x7F || value is 0x0E or 0x0F)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            return;
        }

        output.Add(value);
    }

    private static void DecodeIso2022JpRoman(
        byte value,
        List<byte> output,
        ref Iso2022JpState state,
        ref bool outputFlag)
    {
        if (value == 0x1B)
        {
            state = Iso2022JpState.EscapeStart;
            return;
        }

        outputFlag = false;
        if (value == 0x5C)
        {
            AppendUtf8Scalar(output, 0x00A5);
            return;
        }

        if (value == 0x7E)
        {
            AppendUtf8Scalar(output, 0x203E);
            return;
        }

        if (value > 0x7F || value is 0x0E or 0x0F)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            return;
        }

        output.Add(value);
    }

    private static void DecodeIso2022JpKatakana(
        byte value,
        List<byte> output,
        ref Iso2022JpState state,
        ref bool outputFlag)
    {
        if (value == 0x1B)
        {
            state = Iso2022JpState.EscapeStart;
            return;
        }

        outputFlag = false;
        if (value is >= 0x21 and <= 0x5F)
        {
            AppendUtf8Scalar(output, value - 0x21 + 0xFF61);
            return;
        }

        AppendUtf8Scalar(output, 0xFFFD);
    }

    private static void DecodeIso2022JpLeadByte(
        byte value,
        List<byte> output,
        ref Iso2022JpState state,
        ref byte lead,
        ref bool outputFlag)
    {
        if (value == 0x1B)
        {
            state = Iso2022JpState.EscapeStart;
            return;
        }

        outputFlag = false;
        if (value is >= 0x21 and <= 0x7E)
        {
            lead = value;
            state = Iso2022JpState.TrailByte;
            return;
        }

        AppendUtf8Scalar(output, 0xFFFD);
    }

    private static void DecodeIso2022JpTrailByte(
        byte value,
        List<byte> output,
        ref Iso2022JpState state,
        ref byte lead,
        ref bool outputFlag)
    {
        if (value == 0x1B)
        {
            state = Iso2022JpState.EscapeStart;
            AppendUtf8Scalar(output, 0xFFFD);
            return;
        }

        state = Iso2022JpState.LeadByte;
        outputFlag = false;
        int leadMinusOffset = lead - 0x21;
        int trailMinusOffset = value - 0x21;
        if (leadMinusOffset == 0x03 && (uint)trailMinusOffset < 0x53)
        {
            AppendUtf8Scalar(output, 0x3041 + trailMinusOffset);
            return;
        }

        if (leadMinusOffset == 0x04 && (uint)trailMinusOffset < 0x56)
        {
            AppendUtf8Scalar(output, 0x30A1 + trailMinusOffset);
            return;
        }

        if (trailMinusOffset is < 0 or > 0x5D)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            return;
        }

        int pointer = (leadMinusOffset * 94) + trailMinusOffset;
        if (TryDecodeEucJpJis0208Pointer(pointer, out int scalar))
        {
            AppendUtf8Scalar(output, scalar);
        }
        else
        {
            AppendUtf8Scalar(output, 0xFFFD);
        }
    }

    private static bool TryGetIso2022JpEscapeState(byte lead, byte value, out Iso2022JpState state)
    {
        state = lead switch
        {
            (byte)'(' when value == (byte)'B' => Iso2022JpState.Ascii,
            (byte)'(' when value == (byte)'J' => Iso2022JpState.Roman,
            (byte)'(' when value == (byte)'I' => Iso2022JpState.Katakana,
            (byte)'$' when value is (byte)'@' or (byte)'B' => Iso2022JpState.LeadByte,
            _ => default,
        };
        return state != default || (lead == (byte)'(' && value == (byte)'B');
    }

    private static void PrependIso2022JpByte(
        byte prepended,
        List<byte> output,
        ref Iso2022JpState state,
        ref byte lead)
    {
        switch (state)
        {
            case Iso2022JpState.Ascii:
            case Iso2022JpState.Roman:
                output.Add(prepended);
                break;

            case Iso2022JpState.Katakana:
                AppendUtf8Scalar(output, prepended - 0x21 + 0xFF61);
                break;

            case Iso2022JpState.LeadByte:
                lead = prepended;
                state = Iso2022JpState.TrailByte;
                break;
        }
    }

    private static void FlushIso2022JpEnd(
        List<byte> output,
        ref Iso2022JpState state,
        Iso2022JpState outputState,
        ref byte lead)
    {
        switch (state)
        {
            case Iso2022JpState.TrailByte:
            case Iso2022JpState.EscapeStart:
                state = outputState;
                AppendUtf8Scalar(output, 0xFFFD);
                break;

            case Iso2022JpState.Escape:
                byte prepended = lead;
                state = outputState;
                lead = 0;
                AppendUtf8Scalar(output, 0xFFFD);
                PrependIso2022JpByte(prepended, output, ref state, ref lead);
                if (state == Iso2022JpState.TrailByte)
                {
                    state = outputState;
                    AppendUtf8Scalar(output, 0xFFFD);
                }

                break;

            default:
                break;
        }
    }

    private static byte[] DecodeEucJp(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            if (first == 0x8E)
            {
                DecodeEucJpHalfWidthKatakana(bytes, output, ref index);
                continue;
            }

            if (first == 0x8F)
            {
                DecodeEucJpJis0212(bytes, output, ref index);
                continue;
            }

            int leadMinusOffset = first - 0xA1;
            if (leadMinusOffset is < 0 or > 0x5D)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            byte second = bytes[index + 1];
            int trailMinusOffset = second - 0xA1;
            if (leadMinusOffset == 0x03 && (uint)trailMinusOffset < 0x53)
            {
                AppendUtf8Scalar(output, 0x3041 + trailMinusOffset);
                index += 2;
                continue;
            }

            if (leadMinusOffset == 0x04 && (uint)trailMinusOffset < 0x56)
            {
                AppendUtf8Scalar(output, 0x30A1 + trailMinusOffset);
                index += 2;
                continue;
            }

            if (trailMinusOffset is < 0 or > 0x5D)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index += second < 0x80 ? 1 : 2;
                continue;
            }

            int pointer = (leadMinusOffset * 94) + trailMinusOffset;
            if (TryDecodeEucJpJis0208Pointer(pointer, out int scalar))
            {
                AppendUtf8Scalar(output, scalar);
            }
            else
            {
                AppendUtf8Scalar(output, 0xFFFD);
            }

            index += 2;
        }

        return output.ToArray();
    }

    private static void DecodeEucJpHalfWidthKatakana(ReadOnlySpan<byte> bytes, List<byte> output, ref int index)
    {
        if (index + 1 >= bytes.Length)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index = bytes.Length;
            return;
        }

        byte second = bytes[index + 1];
        int trailMinusOffset = second - 0xA1;
        if (trailMinusOffset is >= 0 and <= 0x3E)
        {
            AppendUtf8Scalar(output, 0xFF61 + trailMinusOffset);
            index += 2;
            return;
        }

        AppendUtf8Scalar(output, 0xFFFD);
        index += second < 0x80 ? 1 : 2;
    }

    private static void DecodeEucJpJis0212(ReadOnlySpan<byte> bytes, List<byte> output, ref int index)
    {
        if (index + 1 >= bytes.Length)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index = bytes.Length;
            return;
        }

        byte lead = bytes[index + 1];
        int leadMinusOffset = lead - 0xA1;
        if (leadMinusOffset is < 0 or > 0x5D)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index += lead < 0x80 ? 1 : 2;
            return;
        }

        if (index + 2 >= bytes.Length)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index = bytes.Length;
            return;
        }

        byte trail = bytes[index + 2];
        int trailMinusOffset = trail - 0xA1;
        if (trailMinusOffset is < 0 or > 0x5D)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index += trail < 0x80 ? 2 : 3;
            return;
        }

        int pointer = (leadMinusOffset * 94) + trailMinusOffset;
        if (TryDecodeJis0212Pointer(pointer, out int scalar))
        {
            AppendUtf8Scalar(output, scalar);
        }
        else
        {
            AppendUtf8Scalar(output, 0xFFFD);
        }

        index += 3;
    }

    private static bool TryDecodeEucJpJis0208Pointer(int pointer, out int scalar)
    {
        int level1Pointer = pointer - 1410;
        ReadOnlySpan<ushort> level1 = SearchJapaneseData.Jis0208Level1Kanji;
        if ((uint)level1Pointer < (uint)level1.Length)
        {
            scalar = level1[level1Pointer];
            return true;
        }

        int level2Pointer = pointer - 4418;
        ReadOnlySpan<ushort> level2 = SearchJapaneseData.Jis0208Level2AndAdditionalKanji;
        if ((uint)level2Pointer < (uint)level2.Length)
        {
            scalar = level2[level2Pointer];
            return true;
        }

        int ibmPointer = pointer - 8272;
        ReadOnlySpan<ushort> ibmKanji = SearchJapaneseData.IbmKanji;
        if ((uint)ibmPointer < (uint)ibmKanji.Length)
        {
            scalar = ibmKanji[ibmPointer];
            return true;
        }

        return TryDecodeJis0208Symbol(pointer, out scalar) ||
            TryDecodeJis0208Range(pointer, out scalar);
    }

    private static bool TryDecodeJis0212Pointer(int pointer, out int scalar)
    {
        int pointerMinusKanji = pointer - 1410;
        ReadOnlySpan<ushort> kanji = SearchJapaneseData.Jis0212Kanji;
        if ((uint)pointerMinusKanji < (uint)kanji.Length)
        {
            scalar = kanji[pointerMinusKanji];
            return true;
        }

        if (TryDecodeJis0212Accented(pointer, out scalar))
        {
            return true;
        }

        int pointerMinusUpperCyrillic = pointer - 597;
        if ((uint)pointerMinusUpperCyrillic <= 10)
        {
            scalar = 0x0402 + pointerMinusUpperCyrillic;
            return true;
        }

        int pointerMinusLowerCyrillic = pointer - 645;
        if ((uint)pointerMinusLowerCyrillic <= 10)
        {
            scalar = 0x0452 + pointerMinusLowerCyrillic;
            return true;
        }

        scalar = 0;
        return false;
    }

    private static bool TryDecodeJis0212Accented(int pointer, out int scalar)
    {
        ReadOnlySpan<ushort> triples = SearchJapaneseData.Jis0212AccentedTriples;
        ReadOnlySpan<ushort> accented = SearchJapaneseData.Jis0212Accented;
        for (int index = 0; index < triples.Length; index += 3)
        {
            int start = triples[index];
            int length = triples[index + 1];
            int pointerMinusStart = pointer - start;
            if ((uint)pointerMinusStart < (uint)length)
            {
                int offset = triples[index + 2];
                scalar = accented[pointerMinusStart + offset];
                return scalar != 0;
            }
        }

        scalar = 0;
        return false;
    }

    private static byte[] DecodeShiftJis(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            if (!TryGetShiftJisLead(first, output, out int leadMinusOffset))
            {
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            byte second = bytes[index + 1];
            int trailMinusHiragana = second - 0x9F;
            if (leadMinusOffset == 0x01 && (uint)trailMinusHiragana < 0x53)
            {
                AppendUtf8Scalar(output, 0x3041 + trailMinusHiragana);
                index += 2;
                continue;
            }

            if (!TryGetShiftJisTrail(second, out int trailMinusOffset))
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index += second < 0x80 ? 1 : 2;
                continue;
            }

            if (leadMinusOffset == 0x02 && trailMinusOffset < 0x56)
            {
                AppendUtf8Scalar(output, 0x30A1 + trailMinusOffset);
                index += 2;
                continue;
            }

            int pointer = (leadMinusOffset * 188) + trailMinusOffset;
            if (TryDecodeJis0208Pointer(pointer, out int scalar))
            {
                AppendUtf8Scalar(output, scalar);
                index += 2;
                continue;
            }

            AppendUtf8Scalar(output, 0xFFFD);
            index += second < 0x80 ? 1 : 2;
        }

        return output.ToArray();
    }

    private static bool TryGetShiftJisLead(byte first, List<byte> output, out int leadMinusOffset)
    {
        int firstMinusOffset = first - 0x81;
        if (firstMinusOffset is >= 0 and <= 0x1E)
        {
            leadMinusOffset = firstMinusOffset;
            return true;
        }

        firstMinusOffset = first - 0xE0;
        if (firstMinusOffset is >= 0 and <= 0x1C)
        {
            leadMinusOffset = first - 0xC1;
            return true;
        }

        int halfWidthKatakana = first - 0xA1;
        if (halfWidthKatakana is >= 0 and <= 0x3E)
        {
            AppendUtf8Scalar(output, 0xFF61 + halfWidthKatakana);
            leadMinusOffset = 0;
            return false;
        }

        AppendUtf8Scalar(output, first == 0x80 ? 0x80 : 0xFFFD);
        leadMinusOffset = 0;
        return false;
    }

    private static bool IsShiftJisLead(byte first)
    {
        return first is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC;
    }

    private static bool TryGetShiftJisTrail(byte second, out int trailMinusOffset)
    {
        if (second is >= 0x40 and <= 0x7E)
        {
            trailMinusOffset = second - 0x40;
            return true;
        }

        if (second is >= 0x80 and <= 0xFC)
        {
            trailMinusOffset = second - 0x41;
            return true;
        }

        trailMinusOffset = 0;
        return false;
    }

    private static bool TryDecodeJis0208Pointer(int pointer, out int scalar)
    {
        int level1Pointer = pointer - 1410;
        ReadOnlySpan<ushort> level1 = SearchJapaneseData.Jis0208Level1Kanji;
        if ((uint)level1Pointer < (uint)level1.Length)
        {
            scalar = level1[level1Pointer];
            return true;
        }

        int level2Pointer = pointer - 4418;
        ReadOnlySpan<ushort> level2 = SearchJapaneseData.Jis0208Level2AndAdditionalKanji;
        if ((uint)level2Pointer < (uint)level2.Length)
        {
            scalar = level2[level2Pointer];
            return true;
        }

        int upperIbmPointer = pointer - 10744;
        ReadOnlySpan<ushort> ibmKanji = SearchJapaneseData.IbmKanji;
        if ((uint)upperIbmPointer < (uint)ibmKanji.Length)
        {
            scalar = ibmKanji[upperIbmPointer];
            return true;
        }

        int lowerIbmPointer = pointer - 8272;
        if ((uint)lowerIbmPointer < (uint)ibmKanji.Length)
        {
            scalar = ibmKanji[lowerIbmPointer];
            return true;
        }

        if (pointer is >= 8836 and <= 10715)
        {
            scalar = 0xE000 - 8836 + pointer;
            return true;
        }

        if (TryDecodeJis0208Symbol(pointer, out scalar) ||
            TryDecodeJis0208Range(pointer, out scalar))
        {
            return true;
        }

        scalar = 0;
        return false;
    }

    private static bool TryDecodeJis0208Symbol(int pointer, out int scalar)
    {
        ReadOnlySpan<ushort> triples = SearchJapaneseData.Jis0208SymbolTriples;
        ReadOnlySpan<ushort> symbols = SearchJapaneseData.Jis0208Symbols;
        for (int index = 0; index < triples.Length; index += 3)
        {
            int start = triples[index];
            int length = triples[index + 1];
            int pointerMinusStart = pointer - start;
            if ((uint)pointerMinusStart < (uint)length)
            {
                int offset = triples[index + 2];
                scalar = symbols[pointerMinusStart + offset];
                return true;
            }
        }

        scalar = 0;
        return false;
    }

    private static bool TryDecodeJis0208Range(int pointer, out int scalar)
    {
        ReadOnlySpan<ushort> triples = SearchJapaneseData.Jis0208RangeTriples;
        for (int index = 0; index < triples.Length; index += 3)
        {
            int start = triples[index];
            int length = triples[index + 1];
            int pointerMinusStart = pointer - start;
            if ((uint)pointerMinusStart < (uint)length)
            {
                int offset = triples[index + 2];
                scalar = pointerMinusStart + offset;
                return true;
            }
        }

        scalar = 0;
        return false;
    }

    private static byte[] DecodeBig5(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            int leadMinusOffset = first - 0x81;
            if (leadMinusOffset is < 0 or > 0x7D)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            byte second = bytes[index + 1];
            if (!TryGetBig5Trail(second, out int trailMinusOffset))
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index += second < 0x80 ? 1 : 2;
                continue;
            }

            int pointer = (leadMinusOffset * 157) + trailMinusOffset;
            int rebasedPointer = pointer - 942;
            ReadOnlySpan<ushort> lowBitsTable = SearchBig5Data.LowBits;
            int lowBits = (uint)rebasedPointer < (uint)lowBitsTable.Length
                ? lowBitsTable[rebasedPointer]
                : 0;
            if (lowBits == 0)
            {
                if (TryAppendBig5Combination(output, pointer))
                {
                    index += 2;
                    continue;
                }

                AppendUtf8Scalar(output, 0xFFFD);
                index += second < 0x80 ? 1 : 2;
                continue;
            }

            AppendUtf8Scalar(output, IsBig5Astral(rebasedPointer) ? lowBits | 0x20000 : lowBits);
            index += 2;
        }

        return output.ToArray();
    }

    private static bool TryGetBig5Trail(byte second, out int trailMinusOffset)
    {
        if (second is >= 0x40 and <= 0x7E)
        {
            trailMinusOffset = second - 0x40;
            return true;
        }

        if (second is >= 0xA1 and <= 0xFE)
        {
            trailMinusOffset = second - 0x62;
            return true;
        }

        trailMinusOffset = 0;
        return false;
    }

    private static bool TryAppendBig5Combination(List<byte> output, int pointer)
    {
        switch (pointer)
        {
            case 1133:
                AppendUtf8Scalar(output, 0x00CA);
                AppendUtf8Scalar(output, 0x0304);
                return true;

            case 1135:
                AppendUtf8Scalar(output, 0x00CA);
                AppendUtf8Scalar(output, 0x030C);
                return true;

            case 1164:
                AppendUtf8Scalar(output, 0x00EA);
                AppendUtf8Scalar(output, 0x0304);
                return true;

            case 1166:
                AppendUtf8Scalar(output, 0x00EA);
                AppendUtf8Scalar(output, 0x030C);
                return true;

            default:
                return false;
        }
    }

    private static bool IsBig5Astral(int rebasedPointer)
    {
        ReadOnlySpan<uint> astralness = SearchBig5Data.Astralness;
        int index = rebasedPointer >> 5;
        return (uint)index < (uint)astralness.Length &&
            (astralness[index] & (1u << (rebasedPointer & 0x1F))) != 0;
    }

    private static byte[] DecodeGb18030(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            int firstMinusOffset = first - 0x81;
            if (firstMinusOffset is < 0 or > 0x7D)
            {
                AppendUtf8Scalar(output, first == 0x80 ? 0x20AC : 0xFFFD);
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            byte second = bytes[index + 1];
            int secondMinusOffset = second - 0x30;
            if (secondMinusOffset is >= 0 and <= 9)
            {
                DecodeGb18030FourByte(bytes, output, firstMinusOffset, secondMinusOffset, ref index);
                continue;
            }

            if (TryDecodeGb18030TwoByte(firstMinusOffset, second, out int scalar))
            {
                AppendUtf8Scalar(output, scalar);
                index += 2;
                continue;
            }

            AppendUtf8Scalar(output, 0xFFFD);
            index += second < 0x80 ? 1 : 2;
        }

        return output.ToArray();
    }

    private static void DecodeGb18030FourByte(
        ReadOnlySpan<byte> bytes,
        List<byte> output,
        int firstMinusOffset,
        int secondMinusOffset,
        ref int index)
    {
        if (index + 2 >= bytes.Length)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index = bytes.Length;
            return;
        }

        byte third = bytes[index + 2];
        int thirdMinusOffset = third - 0x81;
        if (thirdMinusOffset is < 0 or > 0x7D)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            output.Add(bytes[index + 1]);
            index += 2;
            return;
        }

        if (index + 3 >= bytes.Length)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            index = bytes.Length;
            return;
        }

        byte fourth = bytes[index + 3];
        int fourthMinusOffset = fourth - 0x30;
        if (fourthMinusOffset is < 0 or > 9)
        {
            AppendUtf8Scalar(output, 0xFFFD);
            output.Add(bytes[index + 1]);
            index += 2;
            return;
        }

        int pointer = (firstMinusOffset * 10 * 126 * 10) +
            (secondMinusOffset * 10 * 126) +
            (thirdMinusOffset * 10) +
            fourthMinusOffset;
        if (TryDecodeGb18030Range(pointer, out int scalar))
        {
            AppendUtf8Scalar(output, scalar);
        }
        else
        {
            AppendUtf8Scalar(output, 0xFFFD);
        }

        index += 4;
    }

    private static bool TryDecodeGb18030Range(int pointer, out int scalar)
    {
        if (pointer <= 39419)
        {
            scalar = pointer == 7457
                ? 0xE7C7
                : MapWithRanges(SearchGb18030Data.Gb18030RangePointers, SearchGb18030Data.Gb18030RangeOffsets, pointer);
            return true;
        }

        if (pointer is >= 189000 and <= 1237575)
        {
            scalar = pointer - (189000 - 0x10000);
            return true;
        }

        scalar = 0;
        return false;
    }

    private static bool TryDecodeGb18030TwoByte(int firstMinusOffset, byte second, out int scalar)
    {
        if (firstMinusOffset >= 0x20)
        {
            int trailMinusOffset = second - 0xA1;
            if (trailMinusOffset is >= 0 and <= 0x5D)
            {
                return TryDecodeGb2312(firstMinusOffset, trailMinusOffset, out scalar);
            }

            return TryDecodeGbkLeft(firstMinusOffset, second, out scalar);
        }

        return TryDecodeGbkTop(firstMinusOffset, second, out scalar);
    }

    private static bool TryDecodeGb2312(int firstMinusOffset, int trailMinusOffset, out int scalar)
    {
        int hanziLead = firstMinusOffset - 0x2F;
        if ((uint)hanziLead < 0x48)
        {
            int hanziPointer = (hanziLead * 94) + trailMinusOffset;
            scalar = SearchGb18030Data.Gb2312Hanzi[hanziPointer];
            return true;
        }

        if (firstMinusOffset == 0x20)
        {
            scalar = SearchGb18030Data.Gb2312Symbols[trailMinusOffset];
            return true;
        }

        if (firstMinusOffset == 0x25)
        {
            int symbolsAfterGreekPointer = trailMinusOffset - 63;
            ReadOnlySpan<ushort> symbolsAfterGreek = SearchGb18030Data.Gb2312SymbolsAfterGreek;
            if ((uint)symbolsAfterGreekPointer < (uint)symbolsAfterGreek.Length)
            {
                scalar = symbolsAfterGreek[symbolsAfterGreekPointer];
                return true;
            }
        }

        if (firstMinusOffset == 0x27)
        {
            ReadOnlySpan<ushort> pinyin = SearchGb18030Data.Gb2312Pinyin;
            if (trailMinusOffset < pinyin.Length)
            {
                scalar = pinyin[trailMinusOffset];
                return true;
            }
        }

        if (firstMinusOffset > 0x76)
        {
            scalar = 0xE234 + ((firstMinusOffset - 0x77) * 94) + trailMinusOffset;
            return true;
        }

        int otherPointer = ((firstMinusOffset - 0x21) * 94) + trailMinusOffset;
        scalar = MapWithRanges(
            SearchGb18030Data.Gb2312OtherPointers[..^1],
            SearchGb18030Data.Gb2312OtherUnsortedOffsets,
            otherPointer);
        return true;
    }

    private static bool TryDecodeGbkLeft(int firstMinusOffset, byte second, out int scalar)
    {
        if (!TryGetGbkTrail(second, 0xA0, out int trailMinusOffset))
        {
            scalar = 0;
            return false;
        }

        int leftLead = firstMinusOffset - 0x20;
        int leftPointer = (leftLead * (190 - 94)) + trailMinusOffset;
        int gbkLeftIdeographPointer = leftPointer - ((0x29 - 0x20) * (190 - 94));
        if ((uint)gbkLeftIdeographPointer < ((0x7D - 0x29) * (190 - 94)) - 5)
        {
            scalar = MapWithRanges(
                SearchGb18030Data.GbkLeftIdeographPointers,
                SearchGb18030Data.GbkLeftIdeographOffsets,
                gbkLeftIdeographPointer);
            return true;
        }

        if (leftPointer < (0x29 - 0x20) * (190 - 94))
        {
            scalar = MapWithRanges(
                SearchGb18030Data.GbkOtherPointers[..^1],
                SearchGb18030Data.GbkOtherUnsortedOffsets,
                leftPointer);
            return true;
        }

        int bottomPointer = leftPointer - (((0x7D - 0x20) * (190 - 94)) - 5);
        scalar = SearchGb18030Data.GbkBottom[bottomPointer];
        return true;
    }

    private static bool TryDecodeGbkTop(int firstMinusOffset, byte second, out int scalar)
    {
        if (!TryGetGbkTrail(second, 0xFE, out int trailMinusOffset))
        {
            scalar = 0;
            return false;
        }

        int pointer = (firstMinusOffset * 190) + trailMinusOffset;
        scalar = MapWithRanges(
            SearchGb18030Data.GbkTopIdeographPointers,
            SearchGb18030Data.GbkTopIdeographOffsets,
            pointer);
        return true;
    }

    private static bool TryGetGbkTrail(byte second, byte upperRangeEnd, out int trailMinusOffset)
    {
        if (second is >= 0x40 and <= 0x7E)
        {
            trailMinusOffset = second - 0x40;
            return true;
        }

        if (second >= 0x80 && second <= upperRangeEnd)
        {
            trailMinusOffset = second - 0x41;
            return true;
        }

        trailMinusOffset = 0;
        return false;
    }

    private static byte[] DecodeEucKr(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte first = bytes[index];
            if (first <= 0x7F)
            {
                output.Add(first);
                index++;
                continue;
            }

            int leadMinusOffset = first - 0x81;
            if (leadMinusOffset is < 0 or > 0x7D)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                index++;
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                AppendUtf8Scalar(output, 0xFFFD);
                break;
            }

            byte second = bytes[index + 1];
            if (TryDecodeEucKrPair(leadMinusOffset, second, out int scalar))
            {
                AppendUtf8Scalar(output, scalar);
                index += 2;
                continue;
            }

            AppendUtf8Scalar(output, 0xFFFD);
            index += second < 0x80 ? 1 : 2;
        }

        return output.ToArray();
    }

    private static bool TryDecodeEucKrPair(int leadMinusOffset, byte trail, out int scalar)
    {
        if (leadMinusOffset >= 0x20)
        {
            int trailMinusOffset = trail - 0xA1;
            if (trailMinusOffset is >= 0 and <= 0x5D)
            {
                return TryDecodeKsx1001(leadMinusOffset, trailMinusOffset, out scalar);
            }

            return TryDecodeCp949Left(leadMinusOffset, trail, out scalar);
        }

        return TryDecodeCp949Top(leadMinusOffset, trail, out scalar);
    }

    private static bool TryDecodeKsx1001(int leadMinusOffset, int trailMinusOffset, out int scalar)
    {
        int ksxPointer = ((leadMinusOffset - 0x20) * 94) + trailMinusOffset;
        int hangulPointer = ksxPointer - ((0x2F - 0x20) * 94);
        ReadOnlySpan<ushort> hangul = SearchEucKrData.Ksx1001Hangul;
        if ((uint)hangulPointer < (uint)hangul.Length)
        {
            scalar = hangul[hangulPointer];
            return true;
        }

        ReadOnlySpan<ushort> symbols = SearchEucKrData.Ksx1001Symbols;
        if (ksxPointer < symbols.Length)
        {
            scalar = symbols[ksxPointer];
            return true;
        }

        int hanjaPointer = ksxPointer - ((0x49 - 0x20) * 94);
        ReadOnlySpan<ushort> hanja = SearchEucKrData.Ksx1001Hanja;
        if ((uint)hanjaPointer < (uint)hanja.Length)
        {
            scalar = hanja[hanjaPointer];
            return true;
        }

        if (leadMinusOffset == 0x27)
        {
            ReadOnlySpan<ushort> uppercase = SearchEucKrData.Ksx1001Uppercase;
            if (trailMinusOffset < uppercase.Length)
            {
                scalar = uppercase[trailMinusOffset];
                return scalar != 0;
            }
        }

        if (leadMinusOffset == 0x28)
        {
            ReadOnlySpan<ushort> lowercase = SearchEucKrData.Ksx1001Lowercase;
            if (trailMinusOffset < lowercase.Length)
            {
                scalar = lowercase[trailMinusOffset];
                return true;
            }
        }

        if (leadMinusOffset == 0x25)
        {
            ReadOnlySpan<ushort> box = SearchEucKrData.Ksx1001Box;
            if (trailMinusOffset < box.Length)
            {
                scalar = box[trailMinusOffset];
                return true;
            }
        }

        int otherPointer = ksxPointer - (2 * 94);
        if ((uint)otherPointer < 0x039F)
        {
            scalar = MapWithRanges(
                SearchEucKrData.Ksx1001OtherPointers[..^1],
                SearchEucKrData.Ksx1001OtherUnsortedOffsets,
                otherPointer);
            return scalar >= 0x80;
        }

        scalar = 0;
        return false;
    }

    private static bool TryDecodeCp949Left(int leadMinusOffset, byte trail, out int scalar)
    {
        if (!TryGetCp949ExtensionTrail(trail, 0xA0, out int leftTrail))
        {
            scalar = 0;
            return false;
        }

        int leftLead = leadMinusOffset - 0x20;
        int leftPointer = (leftLead * (190 - 94 - 12)) + leftTrail;
        if (leftPointer >= ((0x45 - 0x20) * (190 - 94 - 12)) + 0x12)
        {
            scalar = 0;
            return false;
        }

        scalar = MapWithRanges(
            SearchEucKrData.Cp949LeftHangulPointers,
            SearchEucKrData.Cp949LeftHangulOffsets,
            leftPointer);
        return true;
    }

    private static bool TryDecodeCp949Top(int leadMinusOffset, byte trail, out int scalar)
    {
        if (!TryGetCp949ExtensionTrail(trail, 0xFE, out int topTrail))
        {
            scalar = 0;
            return false;
        }

        int topPointer = (leadMinusOffset * (190 - 12)) + topTrail;
        scalar = MapWithRanges(
            SearchEucKrData.Cp949TopHangulPointers,
            SearchEucKrData.Cp949TopHangulOffsets,
            topPointer);
        return true;
    }

    private static bool TryGetCp949ExtensionTrail(byte trail, byte upperRangeEnd, out int extensionTrail)
    {
        if (trail >= 0x81 && trail <= upperRangeEnd)
        {
            extensionTrail = trail - (12 + 0x41);
            return true;
        }

        if (trail is >= 0x61 and <= 0x7A)
        {
            extensionTrail = trail - (6 + 0x41);
            return true;
        }

        if (trail is >= 0x41 and <= 0x5A)
        {
            extensionTrail = trail - 0x41;
            return true;
        }

        extensionTrail = 0;
        return false;
    }

    private static int MapWithRanges(ReadOnlySpan<ushort> pointers, ReadOnlySpan<ushort> values, int needle)
    {
        int index = BinarySearch(pointers, needle);
        if (index >= 0)
        {
            return values[index];
        }

        int rangeIndex = ~index - 1;
        return values[rangeIndex] + (needle - pointers[rangeIndex]);
    }

    private static int BinarySearch(ReadOnlySpan<ushort> values, int needle)
    {
        int low = 0;
        int high = values.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int value = values[middle];
            if (value == needle)
            {
                return middle;
            }

            if (value < needle)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private static byte[] DecodeUserDefined(ReadOnlySpan<byte> bytes)
    {
        var output = new List<byte>(bytes.Length);
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            AppendUtf8Scalar(output, value < 0x80 ? value : value + 0xF700);
        }

        return output.ToArray();
    }

    private static bool IsContinuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private static void AppendUtf8Scalar(List<byte> output, int scalar)
    {
        if (scalar <= 0x7F)
        {
            output.Add((byte)scalar);
            return;
        }

        if (scalar <= 0x7FF)
        {
            output.Add((byte)(0xC0 | (scalar >> 6)));
            output.Add((byte)(0x80 | (scalar & 0x3F)));
            return;
        }

        if (scalar <= 0xFFFF)
        {
            output.Add((byte)(0xE0 | (scalar >> 12)));
            output.Add((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            output.Add((byte)(0x80 | (scalar & 0x3F)));
            return;
        }

        output.Add((byte)(0xF0 | (scalar >> 18)));
        output.Add((byte)(0x80 | ((scalar >> 12) & 0x3F)));
        output.Add((byte)(0x80 | ((scalar >> 6) & 0x3F)));
        output.Add((byte)(0x80 | (scalar & 0x3F)));
    }
}
