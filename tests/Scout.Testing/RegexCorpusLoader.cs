using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Scout;

internal static class RegexCorpusLoader
{
    private static readonly string CorpusRoot = Path.Combine(FindRepositoryRoot(), "upstream", "regex-1.12.2", "testdata");

    public static IReadOnlyList<string> EnumerateAllCaseKeys()
    {
        var files = new List<string>(Directory.EnumerateFiles(CorpusRoot, "*.toml"));
        files.Sort(StringComparer.Ordinal);

        var keys = new List<string>();
        for (int index = 0; index < files.Count; index++)
        {
            string relativePath = Path.GetFileName(files[index]);
            IReadOnlyList<string> names = EnumerateCaseNames(relativePath);
            for (int nameIndex = 0; nameIndex < names.Count; nameIndex++)
            {
                keys.Add(relativePath + "|" + names[nameIndex]);
            }
        }

        return keys;
    }

    public static IReadOnlyList<string> EnumerateCaseNames(string relativePath)
    {
        string path = Path.Combine(CorpusRoot, relativePath);
        string text = File.ReadAllText(path);
        string[] blocks = text.Split("[[test]]", StringSplitOptions.RemoveEmptyEntries);
        var names = new List<string>();
        for (int index = 0; index < blocks.Length; index++)
        {
            if (ReadOptionalString(blocks[index], "name") is string name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    public static RegexCorpusCase Load(string relativePath, string name)
    {
        string path = Path.Combine(CorpusRoot, relativePath);
        string text = File.ReadAllText(path);
        string block = FindBlock(text, name, path);
        bool unescape = ReadOptionalBool(block, "unescape", path, name) ?? false;
        IReadOnlyList<byte[]> patterns = ReadPatterns(block, "regex", path, name);
        byte[] haystack = ReadBytes(block, "haystack", path, name, unescape);
        RegexMatch[] expectedMatches = ReadExpectedMatches(block, path, name);
        int? matchLimit = ReadOptionalInt(block, "match-limit", path, name);
        byte lineTerminator = ReadOptionalByte(block, "line-terminator", path, name, unescape) ?? (byte)'\n';
        (int boundsStart, int boundsEnd) = ReadOptionalBounds(block, "bounds", path, name) ?? (0, haystack.Length);
        bool anchored = ReadOptionalBool(block, "anchored", path, name) ?? false;
        string? searchKind = ReadOptionalString(block, "search-kind");
        bool earliest = string.Equals(searchKind, "earliest", StringComparison.Ordinal);
        bool overlapping = string.Equals(searchKind, "overlapping", StringComparison.Ordinal);
        string? matchKind = ReadOptionalString(block, "match-kind");
        bool matchKindAll = string.Equals(matchKind, "all", StringComparison.Ordinal);
        bool caseInsensitive = ReadOptionalBool(block, "case-insensitive", path, name) ?? false;
        bool utf8 = ReadOptionalBool(block, "utf8", path, name) ?? true;
        bool unicode = ReadOptionalBool(block, "unicode", path, name) ?? true;
        bool unicodeClasses = unicode && !string.Equals(relativePath, "regex-lite.toml", StringComparison.Ordinal);
        bool compiles = ReadOptionalBool(block, "compiles", path, name) ?? true;
        return new RegexCorpusCase(name, patterns, haystack, expectedMatches, matchLimit, lineTerminator, boundsStart, boundsEnd, anchored, earliest, overlapping, matchKindAll, caseInsensitive, utf8, unicodeClasses, compiles);
    }

    private static string FindBlock(string text, string name, string path)
    {
        string[] blocks = text.Split("[[test]]", StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < blocks.Length; index++)
        {
            string block = blocks[index];
            if (ContainsName(block, name))
            {
                return block;
            }
        }

        throw new InvalidOperationException("Regex corpus case '" + name + "' was not found in " + path + ".");
    }

    private static bool ContainsName(string block, string name)
    {
        return string.Equals(ReadOptionalString(block, "name"), name, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }

    private static string ReadString(string block, string key, string path, string name)
    {
        string? value = ReadOptionalString(block, key);
        if (value is not null)
        {
            return value;
        }

        throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " is missing '" + key + "'.");
    }

    private static byte[] ReadBytes(string block, string key, string path, string name, bool unescape)
    {
        string value = ReadString(block, key, path, name);
        return EncodeCorpusBytes(value, unescape);
    }

    private static List<byte[]> ReadPatterns(string block, string key, string path, string name)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = line[prefix.Length..].Trim();
            if (value.Length > 0 && value[0] == '[')
            {
                List<string> items = ParseTomlStringArray(ReadTomlArrayValue(value, reader));
                var patterns = new List<byte[]>(items.Count);
                for (int index = 0; index < items.Count; index++)
                {
                    patterns.Add(Encoding.UTF8.GetBytes(items[index]));
                }

                return patterns;
            }

            return [Encoding.UTF8.GetBytes(ParseTomlString(value))];
        }

        throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " is missing '" + key + "'.");
    }

    private static string? ReadOptionalString(string block, string key)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            return ParseTomlString(line[prefix.Length..]);
        }

        return null;
    }

    private static int? ReadOptionalInt(string block, string key, string path, string name)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(line.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an invalid '" + key + "'.");
        }

        return null;
    }

    private static bool? ReadOptionalBool(string block, string key, string path, string name)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = line[prefix.Length..].Trim();
            return value switch
            {
                "true" => true,
                "false" => false,
                _ => throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an invalid '" + key + "'."),
            };
        }

        return null;
    }

    private static byte? ReadOptionalByte(string block, string key, string path, string name, bool unescape)
    {
        string? value = ReadOptionalString(block, key);
        if (value is null)
        {
            return null;
        }

        byte[] bytes = EncodeCorpusBytes(value, unescape);
        if (bytes.Length != 1)
        {
            throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an invalid '" + key + "'.");
        }

        return bytes[0];
    }

    private static (int Start, int End)? ReadOptionalBounds(string block, string key, string path, string name)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = ReadTomlArrayValue(line[prefix.Length..], reader);
            if (TryParseBoundsArray(value, out int start, out int end) ||
                TryParseBoundsObject(value, out start, out end))
            {
                return (start, end);
            }

            throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an invalid '" + key + "'.");
        }

        return null;
    }

    private static bool TryParseBoundsArray(string value, out int start, out int end)
    {
        start = 0;
        end = 0;
        int index = SkipWhitespace(value, 0);
        return index < value.Length &&
            value[index] == '[' &&
            TryParseSpan(value, index + 1, out start, out end, out _);
    }

    private static bool TryParseBoundsObject(string value, out int start, out int end)
    {
        start = 0;
        end = 0;
        int startKey = value.IndexOf("start", StringComparison.Ordinal);
        int endKey = value.IndexOf("end", StringComparison.Ordinal);
        if (startKey < 0 || endKey < 0)
        {
            return false;
        }

        if (!TryParseObjectInt(value, startKey + "start".Length, out start) ||
            !TryParseObjectInt(value, endKey + "end".Length, out end))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseObjectInt(string value, int index, out int number)
    {
        number = 0;
        index = SkipWhitespace(value, index);
        if (index >= value.Length || value[index] != '=')
        {
            return false;
        }

        index = SkipWhitespace(value, index + 1);
        return TryReadInt(value, index, out number, out _);
    }

    private static RegexMatch[] ReadExpectedMatches(string block, string path, string name)
    {
        string prefix = "matches = ";
        using var reader = new StringReader(block);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = ReadTomlArrayValue(line[prefix.Length..], reader);
            return ParseMatchSpans(value);
        }

        throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an unsupported matches shape.");
    }

    private static string ReadTomlArrayValue(string firstLine, StringReader reader)
    {
        var builder = new StringBuilder(firstLine.Trim());
        int depth = CountBracketDepth(builder.ToString());
        while (depth > 0 && reader.ReadLine() is string line)
        {
            builder.Append(' ');
            string trimmed = line.Trim();
            builder.Append(trimmed);
            depth += CountBracketDepth(trimmed);
        }

        return builder.ToString();
    }

    private static int CountBracketDepth(string value)
    {
        int depth = 0;
        for (int index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '[' => 1,
                ']' => -1,
                _ => 0,
            };
        }

        return depth;
    }

    private static RegexMatch[] ParseMatchSpans(string value)
    {
        var matches = new List<RegexMatch>();
        int index = SkipWhitespace(value, 0);
        if (index >= value.Length || value[index] != '[')
        {
            throw new InvalidOperationException("Invalid regex corpus matches array.");
        }

        index++;
        while (index < value.Length)
        {
            index = SkipWhitespaceAndCommas(value, index);
            if (index >= value.Length || value[index] == ']')
            {
                break;
            }

            if (value[index] != '[')
            {
                index++;
                continue;
            }

            int itemContent = SkipWhitespace(value, index + 1);
            if (itemContent < value.Length && char.IsDigit(value[itemContent]))
            {
                if (TryParseSpan(value, itemContent, out int start, out int end, out _))
                {
                    matches.Add(new RegexMatch(start, end - start));
                }
            }
            else if (itemContent < value.Length && value[itemContent] == '[')
            {
                int captureStart = SkipWhitespace(value, itemContent + 1);
                if (TryParseSpan(value, captureStart, out int start, out int end, out _))
                {
                    matches.Add(new RegexMatch(start, end - start));
                }
            }

            index = SkipBracketedValue(value, index);
        }

        return matches.ToArray();
    }

    private static int SkipWhitespaceAndCommas(string value, int startIndex)
    {
        int index = startIndex;
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == ','))
        {
            index++;
        }

        return index;
    }

    private static int SkipBracketedValue(string value, int startIndex)
    {
        int depth = 0;
        int index = startIndex;
        while (index < value.Length)
        {
            if (value[index] == '[')
            {
                depth++;
            }
            else if (value[index] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }

            index++;
        }

        return value.Length;
    }

    private static bool TryParseSpan(string value, int startIndex, out int start, out int end, out int nextIndex)
    {
        start = 0;
        end = 0;
        nextIndex = startIndex;
        if (!TryReadInt(value, startIndex, out start, out int afterStart))
        {
            return false;
        }

        int comma = SkipWhitespace(value, afterStart);
        if (comma >= value.Length || value[comma] != ',')
        {
            return false;
        }

        int endIndex = SkipWhitespace(value, comma + 1);
        if (!TryReadInt(value, endIndex, out end, out int afterEnd))
        {
            return false;
        }

        int close = SkipWhitespace(value, afterEnd);
        if (close >= value.Length || value[close] != ']')
        {
            return false;
        }

        nextIndex = close + 1;
        return true;
    }

    private static bool TryReadInt(string value, int startIndex, out int number, out int nextIndex)
    {
        number = 0;
        int end = startIndex;
        while (end < value.Length && char.IsDigit(value[end]))
        {
            end++;
        }

        nextIndex = end;
        return end > startIndex &&
            int.TryParse(value.AsSpan(startIndex, end - startIndex), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static int SkipWhitespace(string value, int startIndex)
    {
        int index = startIndex;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }

    private static string ParseTomlString(string value)
    {
        value = value.Trim();
        if (value.Length < 2)
        {
            throw new InvalidOperationException("Invalid TOML string value.");
        }

        if (value.StartsWith("'''", StringComparison.Ordinal) && value.EndsWith("'''", StringComparison.Ordinal))
        {
            return value[3..^3];
        }

        if (value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        if (value[0] != '"' || value[^1] != '"')
        {
            throw new InvalidOperationException("Unsupported TOML string value.");
        }

        var builder = new StringBuilder(value.Length);
        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            index++;
            if (index >= value.Length - 1)
            {
                throw new InvalidOperationException("Invalid TOML escape sequence.");
            }

            switch (value[index])
            {
                case 'b':
                    builder.Append('\b');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case 'u':
                    if (!TryReadHexScalar(value, index + 1, 4, out int shortScalar))
                    {
                        throw new InvalidOperationException("Invalid TOML Unicode escape sequence.");
                    }

                    builder.Append(char.ConvertFromUtf32(shortScalar));
                    index += 4;
                    break;
                case 'U':
                    if (!TryReadHexScalar(value, index + 1, 8, out int longScalar))
                    {
                        throw new InvalidOperationException("Invalid TOML Unicode escape sequence.");
                    }

                    builder.Append(char.ConvertFromUtf32(longScalar));
                    index += 8;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported TOML escape sequence.");
            }
        }

        return builder.ToString();
    }

    private static byte[] EncodeCorpusBytes(string value, bool unescape)
    {
        if (!unescape)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        var bytes = new List<byte>(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character != '\\')
            {
                if (character > byte.MaxValue)
                {
                    byte[] encoded = Encoding.UTF8.GetBytes(character.ToString());
                    bytes.AddRange(encoded);
                }
                else
                {
                    bytes.Add((byte)character);
                }

                continue;
            }

            index++;
            if (index >= value.Length)
            {
                throw new InvalidOperationException("Invalid regex corpus escape sequence.");
            }

            AppendEscapedByte(value, ref index, bytes);
        }

        return bytes.ToArray();
    }

    private static void AppendEscapedByte(string value, ref int index, List<byte> bytes)
    {
        char escaped = value[index];
        switch (escaped)
        {
            case '0':
                bytes.Add(0);
                break;
            case 'n':
                bytes.Add((byte)'\n');
                break;
            case 'r':
                bytes.Add((byte)'\r');
                break;
            case 't':
                bytes.Add((byte)'\t');
                break;
            case '\\':
                bytes.Add((byte)'\\');
                break;
            case '\'':
                bytes.Add((byte)'\'');
                break;
            case 'x':
                if (index + 2 >= value.Length ||
                    !TryReadHexByte((byte)value[index + 1], (byte)value[index + 2], out byte literal))
                {
                    throw new InvalidOperationException("Invalid regex corpus hexadecimal escape.");
                }

                bytes.Add(literal);
                index += 2;
                break;
            default:
                bytes.Add((byte)escaped);
                break;
        }
    }

    private static List<string> ParseTomlStringArray(string value)
    {
        var items = new List<string>();
        int index = SkipWhitespace(value, 0);
        if (index >= value.Length || value[index] != '[')
        {
            throw new InvalidOperationException("Invalid TOML string array.");
        }

        index++;
        while (true)
        {
            index = SkipWhitespace(value, index);
            if (index >= value.Length)
            {
                throw new InvalidOperationException("Unclosed TOML string array.");
            }

            if (value[index] == ']')
            {
                return items;
            }

            items.Add(ParseTomlStringAt(value, ref index));
            index = SkipWhitespace(value, index);
            if (index < value.Length && value[index] == ',')
            {
                index++;
                continue;
            }

            if (index < value.Length && value[index] == ']')
            {
                return items;
            }

            throw new InvalidOperationException("Invalid TOML string array.");
        }
    }

    private static string ParseTomlStringAt(string value, ref int index)
    {
        int start = index;
        if (StartsWithAt(value, index, "'''"))
        {
            int end = value.IndexOf("'''", index + 3, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidOperationException("Unclosed TOML literal string.");
            }

            index = end + 3;
            return ParseTomlString(value[start..index]);
        }

        if (value[index] == '\'')
        {
            int end = value.IndexOf('\'', index + 1);
            if (end < 0)
            {
                throw new InvalidOperationException("Unclosed TOML literal string.");
            }

            index = end + 1;
            return ParseTomlString(value[start..index]);
        }

        if (value[index] != '"')
        {
            throw new InvalidOperationException("Unsupported TOML string array item.");
        }

        index++;
        while (index < value.Length)
        {
            if (value[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (value[index] == '"')
            {
                index++;
                return ParseTomlString(value[start..index]);
            }

            index++;
        }

        throw new InvalidOperationException("Unclosed TOML basic string.");
    }

    private static bool StartsWithAt(string value, int index, string prefix)
    {
        return index + prefix.Length <= value.Length &&
            string.CompareOrdinal(value, index, prefix, 0, prefix.Length) == 0;
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexValue(high, out int highValue) || !TryGetHexValue(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryReadHexScalar(string value, int start, int length, out int scalar)
    {
        scalar = 0;
        if (start + length > value.Length)
        {
            return false;
        }

        for (int index = start; index < start + length; index++)
        {
            if (value[index] > 0x7F || !TryGetHexValue((byte)value[index], out int digit))
            {
                return false;
            }

            scalar = (scalar << 4) | digit;
        }

        return scalar <= 0x10FFFF && (scalar < 0xD800 || scalar > 0xDFFF);
    }

    private static bool TryGetHexValue(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        digit = 0;
        return false;
    }
}
