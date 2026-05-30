using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Scout;

internal static class DifferentialOutputNormalizer
{
    private static readonly Encoding BytePreserving = Encoding.Latin1;

    public static byte[] NormalizeStdout(byte[] output, DifferentialComparisonMode comparisonMode)
    {
        if (comparisonMode == DifferentialComparisonMode.NonEmptyStdout)
        {
            return output.Length == 0 ? [] : BytePreserving.GetBytes("<non-empty stdout>");
        }

        if (comparisonMode == DifferentialComparisonMode.Exact)
        {
            return output;
        }

        string text = BytePreserving.GetString(output);
        if (comparisonMode is DifferentialComparisonMode.MaskElapsed or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            text = MaskElapsed(text);
        }

        if (comparisonMode is DifferentialComparisonMode.SortLines or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            text = SortOutput(text);
        }

        return BytePreserving.GetBytes(text);
    }

    public static string NormalizeStderr(string error, DifferentialComparisonMode comparisonMode)
    {
        if (comparisonMode == DifferentialComparisonMode.NonEmptyStderr)
        {
            return string.IsNullOrEmpty(error) ? string.Empty : "<non-empty stderr>";
        }

        string normalized = error;
        if (comparisonMode is DifferentialComparisonMode.MaskElapsed or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            normalized = MaskElapsed(normalized);
        }

        if (comparisonMode is DifferentialComparisonMode.SortLines or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            normalized = SortOutput(normalized);
        }

        return normalized;
    }

    private static string MaskElapsed(string text)
    {
        string masked = MaskStatsElapsed(text);
        masked = MaskDurationObjects(masked, "{\"human\":\"", "\",\"nanos\":", ",\"secs\":", "}", "{\"human\":\"0.000000s\",\"nanos\":0,\"secs\":0}");
        return MaskDurationObjects(masked, "{\"secs\":", ",\"nanos\":", ",\"human\":\"", "\"}", "{\"secs\":0,\"nanos\":0,\"human\":\"0.000000s\"}");
    }

    private static string MaskStatsElapsed(string text)
    {
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith(" seconds spent searching", StringComparison.Ordinal))
            {
                lines[index] = "0.000000 seconds spent searching";
            }
            else if (lines[index].EndsWith(" seconds total", StringComparison.Ordinal))
            {
                lines[index] = "0.000000 seconds total";
            }
        }

        return string.Join('\n', lines);
    }

    private static string MaskDurationObjects(string text, string startToken, string middleToken, string endToken, string closeToken, string replacement)
    {
        var builder = new StringBuilder(text.Length);
        int searchIndex = 0;
        while (searchIndex < text.Length)
        {
            int start = text.IndexOf(startToken, searchIndex, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            int middle = text.IndexOf(middleToken, start + startToken.Length, StringComparison.Ordinal);
            if (middle < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            int end = text.IndexOf(endToken, middle + middleToken.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            int close = text.IndexOf(closeToken, end + endToken.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            builder.Append(text, searchIndex, start - searchIndex);
            builder.Append(replacement);
            searchIndex = close + closeToken.Length;
        }

        return builder.ToString();
    }

    private static string SortOutput(string text)
    {
        if (text.Contains('\0') && !text.Contains('\n'))
        {
            return SortNullSeparatedRecords(text);
        }

        if (TrySortHeadingBlocks(text, out string? sorted))
        {
            return sorted;
        }

        return SortLines(text);
    }

    private static bool TrySortHeadingBlocks(string text, [NotNullWhen(true)] out string? sorted)
    {
        sorted = null;
        string[] blocks = text.Split("\n\n", StringSplitOptions.None);
        if (blocks.Length < 2)
        {
            return false;
        }

        bool hasTrailingSeparator = blocks[^1].Length == 0;
        bool hasTrailingLineFeed = text.EndsWith('\n');
        int blockCount = hasTrailingSeparator ? blocks.Length - 1 : blocks.Length;
        if (blockCount < 2)
        {
            return false;
        }

        var sortableBlocks = new List<(string Key, int FirstIndex, string Text)>(blockCount);
        for (int index = 0; index < blockCount; index++)
        {
            string block = TrimTrailingLineFeed(blocks[index]);
            if (!TryGetHeadingBlockKey(block, out string? key))
            {
                return false;
            }

            sortableBlocks.Add((key, index, block));
        }

        sortableBlocks.Sort(static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
            return comparison != 0 ? comparison : left.FirstIndex.CompareTo(right.FirstIndex);
        });

        var builder = new StringBuilder(text.Length);
        for (int index = 0; index < sortableBlocks.Count; index++)
        {
            if (index > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(sortableBlocks[index].Text);
        }

        if (hasTrailingSeparator)
        {
            builder.Append("\n\n");
        }
        else if (hasTrailingLineFeed)
        {
            builder.Append('\n');
        }

        sorted = builder.ToString();
        return true;
    }

    private static string TrimTrailingLineFeed(string block)
    {
        return block.EndsWith('\n') ? block[..^1] : block;
    }

    private static bool TryGetHeadingBlockKey(string block, [NotNullWhen(true)] out string? key)
    {
        key = null;
        if (block.Length == 0)
        {
            return false;
        }

        int lineFeed = block.IndexOf('\n', StringComparison.Ordinal);
        if (lineFeed <= 0 || lineFeed == block.Length - 1)
        {
            return false;
        }

        string firstLine = block[..lineFeed];
        if (TryGetJsonPathKey(firstLine, out _) ||
            firstLine.Length > 0 && firstLine[0] == '{' ||
            firstLine.IndexOf('\0', StringComparison.Ordinal) > 0 ||
            firstLine.IndexOf(':', StringComparison.Ordinal) > 0)
        {
            return false;
        }

        key = firstLine;
        return true;
    }

    private static string SortNullSeparatedRecords(string text)
    {
        string[] split = text.Split('\0');
        bool hasTrailingNull = split.Length > 0 && split[^1].Length == 0;
        int recordCount = hasTrailingNull ? split.Length - 1 : split.Length;
        Array.Sort(split, 0, recordCount, StringComparer.Ordinal);
        string sorted = string.Join('\0', split, 0, recordCount);
        return hasTrailingNull ? sorted + '\0' : sorted;
    }

    private static string SortLines(string text)
    {
        string[] split = text.Split('\n');
        bool hasTrailingLineFeed = split.Length > 0 && split[^1].Length == 0;
        int lineCount = hasTrailingLineFeed ? split.Length - 1 : split.Length;
        var groups = new List<(string Key, int FirstIndex, List<string> Lines)>(lineCount);
        for (int index = 0; index < lineCount; index++)
        {
            string line = split[index];
            string key = GetSortKey(line);
            int groupIndex = FindGroup(groups, key);
            if (groupIndex < 0)
            {
                groups.Add((key, index, [line]));
            }
            else
            {
                groups[groupIndex].Lines.Add(line);
            }
        }

        groups.Sort(static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
            return comparison != 0 ? comparison : left.FirstIndex.CompareTo(right.FirstIndex);
        });

        var lines = new List<string>(lineCount);
        for (int index = 0; index < groups.Count; index++)
        {
            lines.AddRange(groups[index].Lines);
        }

        if (groups.Count == 0)
        {
            return hasTrailingLineFeed ? "\n" : string.Empty;
        }

        string sorted = string.Join('\n', lines);
        return hasTrailingLineFeed ? sorted + "\n" : sorted;
    }

    private static int FindGroup(List<(string Key, int FirstIndex, List<string> Lines)> groups, string key)
    {
        for (int index = 0; index < groups.Count; index++)
        {
            if (string.Equals(groups[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetSortKey(string line)
    {
        if (TryGetJsonPathKey(line, out string? jsonPath))
        {
            return jsonPath;
        }

        if (line.Length > 0 && line[0] == '{')
        {
            return "\uFFFF";
        }

        int nullSeparator = line.IndexOf('\0', StringComparison.Ordinal);
        if (nullSeparator > 0)
        {
            return line[..nullSeparator];
        }

        int pathSeparator = line.IndexOf(':', StringComparison.Ordinal);
        return pathSeparator > 0 ? line[..pathSeparator] : line;
    }

    private static bool TryGetJsonPathKey(string line, [NotNullWhen(true)] out string? path)
    {
        return TryGetJsonStringAfterToken(line, "\"path\":{\"text\":\"", out path) ||
            TryGetJsonStringAfterToken(line, "\"path\":{\"bytes\":\"", out path);
    }

    private static bool TryGetJsonStringAfterToken(string line, string pathToken, [NotNullWhen(true)] out string? path)
    {
        path = null;
        int start = line.IndexOf(pathToken, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += pathToken.Length;
        int end = FindJsonStringEnd(line, start);
        if (end < 0)
        {
            return false;
        }

        path = line[start..end];
        return true;
    }

    private static int FindJsonStringEnd(string text, int start)
    {
        bool escaped = false;
        for (int index = start; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[index] == '"')
            {
                return index;
            }
        }

        return -1;
    }
}
