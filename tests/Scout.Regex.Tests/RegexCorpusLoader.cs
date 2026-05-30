using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Scout;

internal static class RegexCorpusLoader
{
    private const string CorpusRoot = "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata";

    public static RegexCorpusCase Load(string relativePath, string name)
    {
        string path = Path.Combine(CorpusRoot, relativePath);
        string text = File.ReadAllText(path);
        string block = FindBlock(text, name, path);
        string pattern = ReadString(block, "regex", path, name);
        string haystack = ReadString(block, "haystack", path, name);
        RegexMatch[] expectedMatches = ReadExpectedMatches(block, path, name);
        int? matchLimit = ReadOptionalInt(block, "match-limit", path, name);
        return new RegexCorpusCase(name, pattern, haystack, expectedMatches, matchLimit);
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

    private static string ReadString(string block, string key, string path, string name)
    {
        string? value = ReadOptionalString(block, key);
        if (value is not null)
        {
            return value;
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
        int index = 0;
        while (index < value.Length)
        {
            int open = value.IndexOf('[', index);
            if (open < 0)
            {
                break;
            }

            int itemStart = open + 1;
            while (itemStart < value.Length && char.IsWhiteSpace(value[itemStart]))
            {
                itemStart++;
            }

            if (itemStart >= value.Length || !char.IsDigit(value[itemStart]))
            {
                index = open + 1;
                continue;
            }

            if (!TryParseSpan(value, itemStart, out int start, out int end, out int nextIndex))
            {
                index = open + 1;
                continue;
            }

            matches.Add(new RegexMatch(start, end - start));
            index = nextIndex;
        }

        return matches.ToArray();
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

            builder.Append(value[index] switch
            {
                'b' => '\b',
                't' => '\t',
                'n' => '\n',
                'f' => '\f',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                _ => throw new InvalidOperationException("Unsupported TOML escape sequence."),
            });
        }

        return builder.ToString();
    }
}
