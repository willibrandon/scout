using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Escapes and unescapes byte strings with ripgrep CLI semantics.
/// </summary>
public static class CliByteEscape
{
    /// <summary>
    /// Escapes arbitrary bytes using the same representation as bstr's escape_bytes.
    /// </summary>
    /// <param name="bytes">The byte string to escape.</param>
    /// <returns>The escaped representation.</returns>
    public static string Escape(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        int index = 0;
        while (index < bytes.Length)
        {
            byte value = bytes[index];
            if (value <= 0x7F)
            {
                AppendEscapedByte(builder, value);
                index++;
                continue;
            }

            OperationStatus status = Rune.DecodeFromUtf8(bytes[index..], out Rune rune, out int consumed);
            if (status == OperationStatus.Done && consumed > 1)
            {
                builder.Append(rune.ToString());
                index += consumed;
                continue;
            }

            AppendEscapedByte(builder, value);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Unescapes a UTF-16 string into bytes using ripgrep CLI semantics.
    /// </summary>
    /// <param name="text">The escaped text.</param>
    /// <returns>The unescaped bytes.</returns>
    public static byte[] Unescape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Unescape(Utf8.GetBytes(text));
    }

    /// <summary>
    /// Unescapes UTF-8 command-line bytes using ripgrep CLI semantics.
    /// </summary>
    /// <param name="textBytes">The escaped UTF-8 bytes.</param>
    /// <returns>The unescaped bytes.</returns>
    public static byte[] Unescape(ReadOnlySpan<byte> textBytes)
    {
        var bytes = new List<byte>(textBytes.Length);
        for (int index = 0; index < textBytes.Length;)
        {
            byte current = textBytes[index];
            if (current != (byte)'\\')
            {
                bytes.Add(current);
                index++;
                continue;
            }

            if (index + 1 >= textBytes.Length)
            {
                bytes.Add(current);
                index++;
                continue;
            }

            byte escaped = textBytes[index + 1];
            switch (escaped)
            {
                case (byte)'0':
                    bytes.Add(0);
                    index += 2;
                    continue;

                case (byte)'\\':
                    bytes.Add((byte)'\\');
                    index += 2;
                    continue;

                case (byte)'r':
                    bytes.Add((byte)'\r');
                    index += 2;
                    continue;

                case (byte)'n':
                    bytes.Add((byte)'\n');
                    index += 2;
                    continue;

                case (byte)'t':
                    bytes.Add((byte)'\t');
                    index += 2;
                    continue;

                case (byte)'x':
                    if (index + 3 < textBytes.Length &&
                        TryGetHexValue(textBytes[index + 2], out byte high) &&
                        TryGetHexValue(textBytes[index + 3], out byte low))
                    {
                        bytes.Add((byte)((high << 4) | low));
                        index += 4;
                        continue;
                    }

                    bytes.Add(current);
                    index++;
                    continue;

                default:
                    bytes.Add(current);
                    index++;
                    continue;
            }
        }

        return bytes.ToArray();
    }

    private static void AppendEscapedByte(StringBuilder builder, byte value)
    {
        switch (value)
        {
            case >= 0x21 and <= 0x5B:
            case >= 0x5D and <= 0x7E:
                builder.Append((char)value);
                break;

            case 0:
                builder.Append(@"\0");
                break;

            case (byte)'\n':
                builder.Append(@"\n");
                break;

            case (byte)'\r':
                builder.Append(@"\r");
                break;

            case (byte)'\t':
                builder.Append(@"\t");
                break;

            case (byte)'\\':
                builder.Append(@"\\");
                break;

            default:
                builder.Append(@"\x");
                builder.Append(GetHexDigit(value >> 4));
                builder.Append(GetHexDigit(value & 0x0F));
                break;
        }
    }

    private static char GetHexDigit(int value)
    {
        return value < 10
            ? (char)('0' + value)
            : (char)('A' + value - 10);
    }

    private static bool TryGetHexValue(byte value, out byte digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = (byte)(value - (byte)'0');
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = (byte)(value - (byte)'a' + 10);
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = (byte)(value - (byte)'A' + 10);
            return true;
        }

        digit = 0;
        return false;
    }

    private static readonly UTF8Encoding Utf8 =
        new(encoderShouldEmitUTF8Identifier: false);
}
