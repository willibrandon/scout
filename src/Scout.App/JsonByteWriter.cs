using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Scout;

internal static class JsonByteWriter
{
    private static readonly byte[] Hex = "0123456789abcdef"u8.ToArray();
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static void WriteBeginMessage(RawByteWriter output, ReadOnlySpan<byte> path)
    {
        output.Write("{\"type\":\"begin\",\"data\":{\"path\":"u8);
        WriteData(output, path);
        output.Write("}}\n"u8);
    }

    public static void WriteMatchMessage(
        RawByteWriter output,
        ReadOnlySpan<byte> path,
        ReadOnlySpan<byte> line,
        long lineNumber,
        long absoluteOffset,
        IReadOnlyList<JsonMatchSpan> matches)
    {
        output.Write("{\"type\":\"match\",\"data\":{\"path\":"u8);
        WriteData(output, path);
        output.Write(",\"lines\":"u8);
        WriteData(output, line);
        output.Write(",\"line_number\":"u8);
        WriteNumber(output, lineNumber);
        output.Write(",\"absolute_offset\":"u8);
        WriteNumber(output, absoluteOffset);
        output.Write(",\"submatches\":"u8);
        WriteSubmatches(output, line, matches);
        output.Write("}}\n"u8);
    }

    public static void WriteContextMessage(
        RawByteWriter output,
        ReadOnlySpan<byte> path,
        ReadOnlySpan<byte> line,
        long lineNumber,
        long absoluteOffset,
        IReadOnlyList<JsonMatchSpan> matches)
    {
        output.Write("{\"type\":\"context\",\"data\":{\"path\":"u8);
        WriteData(output, path);
        output.Write(",\"lines\":"u8);
        WriteData(output, line);
        output.Write(",\"line_number\":"u8);
        WriteNumber(output, lineNumber);
        output.Write(",\"absolute_offset\":"u8);
        WriteNumber(output, absoluteOffset);
        output.Write(",\"submatches\":"u8);
        WriteSubmatches(output, line, matches);
        output.Write("}}\n"u8);
    }

    public static void WriteEndMessage(RawByteWriter output, ReadOnlySpan<byte> path, int binaryOffset, SearchStats stats)
    {
        output.Write("{\"type\":\"end\",\"data\":{\"path\":"u8);
        WriteData(output, path);
        output.Write(",\"binary_offset\":"u8);
        if (binaryOffset < 0)
        {
            output.Write("null"u8);
        }
        else
        {
            WriteNumber(output, binaryOffset);
        }

        output.Write(",\"stats\":"u8);
        WriteEndStats(output, stats);
        output.Write("}}\n"u8);
    }

    public static void WriteSummaryMessage(RawByteWriter output, SearchStats stats, TimeSpan elapsedTotal)
    {
        output.Write("{\"data\":{\"elapsed_total\":"u8);
        WriteSummaryDuration(output, elapsedTotal);
        output.Write(",\"stats\":"u8);
        WriteSummaryStats(output, stats);
        output.Write("},\"type\":\"summary\"}\n"u8);
    }

    private static void WriteSubmatches(RawByteWriter output, ReadOnlySpan<byte> line, IReadOnlyList<JsonMatchSpan> matches)
    {
        output.Write("["u8);
        for (int index = 0; index < matches.Count; index++)
        {
            if (index > 0)
            {
                output.Write(","u8);
            }

            JsonMatchSpan match = matches[index];
            output.Write("{\"match\":"u8);
            WriteData(output, line.Slice(match.Start, match.End - match.Start));
            if (match.Replacement is not null)
            {
                output.Write(",\"replacement\":"u8);
                WriteData(output, match.Replacement);
            }

            output.Write(",\"start\":"u8);
            WriteNumber(output, match.Start);
            output.Write(",\"end\":"u8);
            WriteNumber(output, match.End);
            output.Write("}"u8);
        }

        output.Write("]"u8);
    }

    private static void WriteEndStats(RawByteWriter output, SearchStats stats)
    {
        output.Write("{\"elapsed\":"u8);
        WriteEndDuration(output, stats.ElapsedNanoseconds);
        output.Write(",\"searches\":"u8);
        WriteNumber(output, stats.Searches);
        output.Write(",\"searches_with_match\":"u8);
        WriteNumber(output, stats.SearchesWithMatch);
        output.Write(",\"bytes_searched\":"u8);
        WriteNumber(output, stats.BytesSearched);
        output.Write(",\"bytes_printed\":"u8);
        WriteNumber(output, stats.BytesPrinted);
        output.Write(",\"matched_lines\":"u8);
        WriteNumber(output, stats.MatchedLines);
        output.Write(",\"matches\":"u8);
        WriteNumber(output, stats.Matches);
        output.Write("}"u8);
    }

    private static void WriteSummaryStats(RawByteWriter output, SearchStats stats)
    {
        output.Write("{\"bytes_printed\":"u8);
        WriteNumber(output, stats.BytesPrinted);
        output.Write(",\"bytes_searched\":"u8);
        WriteNumber(output, stats.BytesSearched);
        output.Write(",\"elapsed\":"u8);
        WriteSummaryDuration(output, stats.ElapsedNanoseconds);
        output.Write(",\"matched_lines\":"u8);
        WriteNumber(output, stats.MatchedLines);
        output.Write(",\"matches\":"u8);
        WriteNumber(output, stats.Matches);
        output.Write(",\"searches\":"u8);
        WriteNumber(output, stats.Searches);
        output.Write(",\"searches_with_match\":"u8);
        WriteNumber(output, stats.SearchesWithMatch);
        output.Write("}"u8);
    }

    private static void WriteEndDuration(RawByteWriter output, ulong nanoseconds)
    {
        output.Write("{\"secs\":"u8);
        WriteNumber(output, nanoseconds / 1_000_000_000UL);
        output.Write(",\"nanos\":"u8);
        WriteNumber(output, nanoseconds % 1_000_000_000UL);
        output.Write(",\"human\":\""u8);
        WriteDurationHuman(output, nanoseconds);
        output.Write("\"}"u8);
    }

    private static void WriteSummaryDuration(RawByteWriter output, TimeSpan duration)
    {
        WriteSummaryDuration(output, (ulong)(duration.Ticks * 100));
    }

    private static void WriteSummaryDuration(RawByteWriter output, ulong nanoseconds)
    {
        output.Write("{\"human\":\""u8);
        WriteDurationHuman(output, nanoseconds);
        output.Write("\",\"nanos\":"u8);
        WriteNumber(output, nanoseconds % 1_000_000_000UL);
        output.Write(",\"secs\":"u8);
        WriteNumber(output, nanoseconds / 1_000_000_000UL);
        output.Write("}"u8);
    }

    private static void WriteData(RawByteWriter output, ReadOnlySpan<byte> bytes)
    {
        if (IsValidUtf8(bytes))
        {
            output.Write("{\"text\":\""u8);
            WriteEscapedJsonString(output, bytes);
            output.Write("\"}"u8);
            return;
        }

        output.Write("{\"bytes\":\""u8);
        WriteAscii(output, Convert.ToBase64String(bytes));
        output.Write("\"}"u8);
    }

    private static void WriteEscapedJsonString(RawByteWriter output, ReadOnlySpan<byte> bytes)
    {
        int written = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            ReadOnlySpan<byte> escape = GetSimpleEscape(bytes[index]);
            if (!escape.IsEmpty)
            {
                output.Write(bytes[written..index]);
                output.Write(escape);
                written = index + 1;
                continue;
            }

            if (bytes[index] < 0x20)
            {
                output.Write(bytes[written..index]);
                output.Write("\\u00"u8);
                output.Write([Hex[bytes[index] >> 4], Hex[bytes[index] & 0x0F]]);
                written = index + 1;
            }
        }

        output.Write(bytes[written..]);
    }

    private static ReadOnlySpan<byte> GetSimpleEscape(byte value)
    {
        return value switch
        {
            (byte)'\"' => "\\\""u8,
            (byte)'\\' => "\\\\"u8,
            (byte)'\n' => "\\n"u8,
            (byte)'\r' => "\\r"u8,
            (byte)'\t' => "\\t"u8,
            0x08 => "\\b"u8,
            0x0C => "\\f"u8,
            _ => [],
        };
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
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

            if (first is >= 0xC2 and <= 0xDF)
            {
                if (!HasContinuation(bytes, index, 1))
                {
                    return false;
                }

                index += 2;
                continue;
            }

            if (first == 0xE0)
            {
                if (index + 2 >= bytes.Length || bytes[index + 1] is < 0xA0 or > 0xBF || !IsContinuation(bytes[index + 2]))
                {
                    return false;
                }

                index += 3;
                continue;
            }

            if (first is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF)
            {
                if (!HasContinuation(bytes, index, 2))
                {
                    return false;
                }

                index += 3;
                continue;
            }

            if (first == 0xED)
            {
                if (index + 2 >= bytes.Length || bytes[index + 1] is < 0x80 or > 0x9F || !IsContinuation(bytes[index + 2]))
                {
                    return false;
                }

                index += 3;
                continue;
            }

            if (first == 0xF0)
            {
                if (index + 3 >= bytes.Length || bytes[index + 1] is < 0x90 or > 0xBF || !IsContinuation(bytes[index + 2]) || !IsContinuation(bytes[index + 3]))
                {
                    return false;
                }

                index += 4;
                continue;
            }

            if (first is >= 0xF1 and <= 0xF3)
            {
                if (!HasContinuation(bytes, index, 3))
                {
                    return false;
                }

                index += 4;
                continue;
            }

            if (first == 0xF4)
            {
                if (index + 3 >= bytes.Length || bytes[index + 1] is < 0x80 or > 0x8F || !IsContinuation(bytes[index + 2]) || !IsContinuation(bytes[index + 3]))
                {
                    return false;
                }

                index += 4;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool HasContinuation(ReadOnlySpan<byte> bytes, int index, int count)
    {
        if (index + count >= bytes.Length)
        {
            return false;
        }

        for (int offset = 1; offset <= count; offset++)
        {
            if (!IsContinuation(bytes[index + offset]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsContinuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private static void WriteDurationHuman(RawByteWriter output, ulong nanoseconds)
    {
        double seconds = nanoseconds / 1_000_000_000.0;
        WriteAscii(output, seconds.ToString("0.000000", CultureInfo.InvariantCulture));
        output.Write("s"u8);
    }

    private static void WriteNumber(RawByteWriter output, long value)
    {
        WriteAscii(output, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteNumber(RawByteWriter output, int value)
    {
        WriteAscii(output, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteNumber(RawByteWriter output, ulong value)
    {
        WriteAscii(output, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteAscii(RawByteWriter output, string value)
    {
        output.Write(Ascii.GetBytes(value));
    }
}
