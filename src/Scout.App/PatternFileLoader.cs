using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Scout;

internal static class PatternFileLoader
{
    public static bool TryLoad(OsString argument, List<byte[]> patterns, Stream standardInput, DiagnosticMessenger diagnostics)
    {
        if (!SearchPathArgument.TryGetText(argument, diagnostics, out string path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = path == "-"
                ? ReadAllBytes(standardInput)
                : File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: No such file or directory (os error 2)").WithContext("rg"));
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: No such file or directory (os error 2)").WithContext("rg"));
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: Permission denied (os error 13)").WithContext("rg"));
            return false;
        }
        catch (IOException exception)
        {
            diagnostics.ErrorMessage(new ScoutError($"{path}: {exception.Message}").WithContext("rg"));
            return false;
        }

        return TryAddPatterns(path, bytes, patterns, diagnostics);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool TryAddPatterns(
        string path,
        byte[] bytes,
        List<byte[]> patterns,
        DiagnosticMessenger diagnostics)
    {
        int lineStart = 0;
        int lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line;
            if (lineFeed < 0)
            {
                line = remaining;
                lineStart = bytes.Length;
            }
            else
            {
                line = remaining[..lineFeed];
                if (!line.IsEmpty && line[^1] == (byte)'\r')
                {
                    line = line[..^1];
                }

                lineStart += lineFeed + 1;
            }

            if (!TryValidateLine(path, lineNumber, line, diagnostics))
            {
                return false;
            }

            patterns.Add(line.ToArray());
            lineNumber++;
        }

        return true;
    }

    private static bool TryValidateLine(
        string path,
        int lineNumber,
        ReadOnlySpan<byte> line,
        DiagnosticMessenger diagnostics)
    {
        if (!TryGetUtf8InvalidOffset(line, out int invalidOffset))
        {
            return true;
        }

        string escaped = EscapeLine(line);
        diagnostics.ErrorMessage(new ScoutError(
            $"{path}:{lineNumber}: found invalid UTF-8 in pattern at byte offset {invalidOffset}: {escaped} (disable Unicode mode and use hex escape sequences to match arbitrary bytes in a pattern, e.g., '(?-u)\\xFF')").WithContext("rg"));
        return false;
    }

    private static bool TryGetUtf8InvalidOffset(ReadOnlySpan<byte> bytes, out int invalidOffset)
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

            int length = GetUtf8SequenceLength(bytes[index..]);
            if (length == 0 || index + length > bytes.Length)
            {
                invalidOffset = index;
                return true;
            }

            for (int continuation = 1; continuation < length; continuation++)
            {
                if (!IsUtf8Continuation(bytes[index + continuation]))
                {
                    invalidOffset = index;
                    return true;
                }
            }

            index += length;
        }

        invalidOffset = -1;
        return false;
    }

    private static int GetUtf8SequenceLength(ReadOnlySpan<byte> bytes)
    {
        byte first = bytes[0];
        if (first is >= 0xC2 and <= 0xDF)
        {
            return 2;
        }

        if (first == 0xE0)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0xA0 and <= 0xBF ? 3 : 0;
        }

        if (first is >= 0xE1 and <= 0xEC or >= 0xEE and <= 0xEF)
        {
            return 3;
        }

        if (first == 0xED)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x80 and <= 0x9F ? 3 : 0;
        }

        if (first == 0xF0)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x90 and <= 0xBF ? 4 : 0;
        }

        if (first is >= 0xF1 and <= 0xF3)
        {
            return 4;
        }

        if (first == 0xF4)
        {
            return bytes.Length >= 2 && bytes[1] is >= 0x80 and <= 0x8F ? 4 : 0;
        }

        return 0;
    }

    private static bool IsUtf8Continuation(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private static string EscapeLine(ReadOnlySpan<byte> line)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < line.Length; index++)
        {
            byte value = line[index];
            if (value is >= 0x20 and <= 0x7E && value != (byte)'\\')
            {
                builder.Append((char)value);
            }
            else if (value == (byte)'\\')
            {
                builder.Append(@"\\");
            }
            else
            {
                builder.Append(@"\x");
                builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
