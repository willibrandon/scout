using System;
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
        RegexMatch? expectedMatch = ReadExpectedMatch(block, path, name);
        return new RegexCorpusCase(name, pattern, haystack, expectedMatch);
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

    private static RegexMatch? ReadExpectedMatch(string block, string path, string name)
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

            string value = line[prefix.Length..].Trim();
            if (value == "[]")
            {
                return null;
            }

            if (!TryParseFirstSpan(value, out int start, out int end))
            {
                break;
            }

            return new RegexMatch(start, end - start);
        }

        throw new InvalidOperationException("Regex corpus case '" + name + "' in " + path + " has an unsupported matches shape.");
    }

    private static bool TryParseFirstSpan(string value, out int start, out int end)
    {
        start = 0;
        end = 0;
        int open = value.IndexOf("[[", StringComparison.Ordinal);
        if (open < 0)
        {
            return false;
        }

        int comma = value.IndexOf(',', open + 2);
        int close = value.IndexOf(']', comma + 1);
        if (comma < 0 || close < 0)
        {
            return false;
        }

        return int.TryParse(value.AsSpan(open + 2, comma - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out start) &&
            int.TryParse(value.AsSpan(comma + 1, close - comma - 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out end);
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
