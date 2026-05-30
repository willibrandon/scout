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

        string sortableText = text;
        string footer = string.Empty;
        if (TrySplitStatsFooter(text, out string? body, out string? statsFooter))
        {
            sortableText = body;
            footer = statsFooter;
        }

        if (TrySortJsonRecords(sortableText, out string? sorted))
        {
            return sorted + footer;
        }

        if (TrySortHeadingBlocks(sortableText, out sorted))
        {
            return sorted + footer;
        }

        if (TrySortContextSeparatedChunks(sortableText, out sorted))
        {
            return sorted + footer;
        }

        if (TrySortPathLineGroups(sortableText, out sorted))
        {
            return sorted + footer;
        }

        return SortLines(sortableText) + footer;
    }

    private static bool TrySplitStatsFooter(string text, [NotNullWhen(true)] out string? body, [NotNullWhen(true)] out string? footer)
    {
        body = null;
        footer = null;
        int separator = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (separator < 0 || separator + 2 >= text.Length)
        {
            return false;
        }

        string candidate = text[(separator + 2)..];
        if (!LooksLikeStatsFooter(candidate))
        {
            return false;
        }

        body = text[..separator];
        footer = text[separator..];
        return true;
    }

    private static bool LooksLikeStatsFooter(string text)
    {
        string[] lines = text.Split('\n');
        bool sawBytesSearched = false;
        bool sawTotal = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length == 0 && index == lines.Length - 1)
            {
                continue;
            }

            if (!IsStatsLine(line))
            {
                return false;
            }

            sawBytesSearched |= line.EndsWith(" bytes searched", StringComparison.Ordinal);
            sawTotal |= line.EndsWith(" seconds total", StringComparison.Ordinal);
        }

        return sawBytesSearched && sawTotal;
    }

    private static bool IsStatsLine(string line)
    {
        return line.EndsWith(" match", StringComparison.Ordinal) ||
            line.EndsWith(" matches", StringComparison.Ordinal) ||
            line.EndsWith(" matched line", StringComparison.Ordinal) ||
            line.EndsWith(" matched lines", StringComparison.Ordinal) ||
            line.EndsWith(" file contained matches", StringComparison.Ordinal) ||
            line.EndsWith(" files contained matches", StringComparison.Ordinal) ||
            line.EndsWith(" file searched", StringComparison.Ordinal) ||
            line.EndsWith(" files searched", StringComparison.Ordinal) ||
            line.EndsWith(" byte printed", StringComparison.Ordinal) ||
            line.EndsWith(" bytes printed", StringComparison.Ordinal) ||
            line.EndsWith(" byte searched", StringComparison.Ordinal) ||
            line.EndsWith(" bytes searched", StringComparison.Ordinal) ||
            line.EndsWith(" seconds spent searching", StringComparison.Ordinal) ||
            line.EndsWith(" seconds total", StringComparison.Ordinal);
    }

    private static bool TrySortJsonRecords(string text, [NotNullWhen(true)] out string? sorted)
    {
        sorted = null;
        string[] split = text.Split('\n');
        bool hasTrailingLineFeed = split.Length > 0 && split[^1].Length == 0;
        int lineCount = hasTrailingLineFeed ? split.Length - 1 : split.Length;
        var groups = new List<(string Key, int FirstIndex, List<string> Lines)>(lineCount);
        var footer = new List<string>();
        bool sawPathRecord = false;
        for (int index = 0; index < lineCount; index++)
        {
            string line = split[index];
            if (line.Length == 0)
            {
                footer.Add(line);
                continue;
            }

            if (line[0] != '{')
            {
                return false;
            }

            if (!TryGetJsonPathKey(line, out string? key))
            {
                footer.Add(line);
                continue;
            }

            sawPathRecord = true;
            AddLineToGroup(groups, key, index, line);
        }

        if (!sawPathRecord)
        {
            return false;
        }

        SortGroups(groups);
        var lines = new List<string>(lineCount);
        AddGroupsToLines(groups, lines);
        lines.AddRange(footer);
        sorted = JoinLines(lines, hasTrailingLineFeed);
        return true;
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

    private static bool TrySortContextSeparatedChunks(string text, [NotNullWhen(true)] out string? sorted)
    {
        sorted = null;
        string[] split = text.Split('\n');
        bool hasTrailingLineFeed = split.Length > 0 && split[^1].Length == 0;
        int lineCount = hasTrailingLineFeed ? split.Length - 1 : split.Length;
        var chunks = new List<(string Key, int FirstIndex, List<string> Lines)>();
        var currentLines = new List<string>();
        string? currentKey = null;
        int currentFirstIndex = -1;
        bool sawSeparator = false;
        bool endedWithSeparator = false;
        for (int index = 0; index < lineCount; index++)
        {
            string line = split[index];
            if (line == "--")
            {
                sawSeparator = true;
                endedWithSeparator = true;
                if (currentLines.Count == 0 || currentKey is null)
                {
                    return false;
                }

                chunks.Add((currentKey, currentFirstIndex, currentLines));
                currentLines = [];
                currentKey = null;
                currentFirstIndex = -1;
                continue;
            }

            if (!TryGetPathSortKey(line, allowContextSeparator: true, out string? key))
            {
                return false;
            }

            endedWithSeparator = false;
            if (currentKey is null)
            {
                currentKey = key;
                currentFirstIndex = index;
            }
            else if (!string.Equals(currentKey, key, StringComparison.Ordinal))
            {
                return false;
            }

            currentLines.Add(line);
        }

        if (!sawSeparator)
        {
            return false;
        }

        if (currentLines.Count > 0)
        {
            chunks.Add((currentKey!, currentFirstIndex, currentLines));
        }

        if (chunks.Count < 2)
        {
            return false;
        }

        chunks.Sort(static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
            return comparison != 0 ? comparison : left.FirstIndex.CompareTo(right.FirstIndex);
        });

        var lines = new List<string>(lineCount);
        for (int index = 0; index < chunks.Count; index++)
        {
            if (index > 0)
            {
                lines.Add("--");
            }

            lines.AddRange(chunks[index].Lines);
        }

        if (endedWithSeparator)
        {
            lines.Add("--");
        }

        sorted = JoinLines(lines, hasTrailingLineFeed);
        return true;
    }

    private static bool TrySortPathLineGroups(string text, [NotNullWhen(true)] out string? sorted)
    {
        sorted = null;
        string[] split = text.Split('\n');
        bool hasTrailingLineFeed = split.Length > 0 && split[^1].Length == 0;
        int lineCount = hasTrailingLineFeed ? split.Length - 1 : split.Length;
        var groups = new List<(string Key, int FirstIndex, List<string> Lines)>(lineCount);
        for (int index = 0; index < lineCount; index++)
        {
            string line = split[index];
            if (!TryGetPathSortKey(line, allowContextSeparator: false, out string? key))
            {
                return false;
            }

            AddLineToGroup(groups, key, index, line);
        }

        if (groups.Count < 2)
        {
            return false;
        }

        SortGroups(groups);
        var lines = new List<string>(lineCount);
        AddGroupsToLines(groups, lines);
        sorted = JoinLines(lines, hasTrailingLineFeed);
        return true;
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
            AddLineToGroup(groups, key, index, line);
        }

        SortGroups(groups);

        var lines = new List<string>(lineCount);
        AddGroupsToLines(groups, lines);

        if (groups.Count == 0)
        {
            return hasTrailingLineFeed ? "\n" : string.Empty;
        }

        return JoinLines(lines, hasTrailingLineFeed);
    }

    private static void AddLineToGroup(List<(string Key, int FirstIndex, List<string> Lines)> groups, string key, int lineIndex, string line)
    {
        int groupIndex = FindGroup(groups, key);
        if (groupIndex < 0)
        {
            groups.Add((key, lineIndex, [line]));
        }
        else
        {
            groups[groupIndex].Lines.Add(line);
        }
    }

    private static void SortGroups(List<(string Key, int FirstIndex, List<string> Lines)> groups)
    {
        groups.Sort(static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
            return comparison != 0 ? comparison : left.FirstIndex.CompareTo(right.FirstIndex);
        });
    }

    private static void AddGroupsToLines(List<(string Key, int FirstIndex, List<string> Lines)> groups, List<string> lines)
    {
        for (int index = 0; index < groups.Count; index++)
        {
            lines.AddRange(groups[index].Lines);
        }
    }

    private static string JoinLines(List<string> lines, bool hasTrailingLineFeed)
    {
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
        if (TryGetPathSortKey(line, allowContextSeparator: false, out string? path))
        {
            return path;
        }

        if (line.Length > 0 && line[0] == '{')
        {
            return "\uFFFF";
        }

        return line;
    }

    private static bool TryGetPathSortKey(string line, bool allowContextSeparator, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (TryGetJsonPathKey(line, out string? jsonPath))
        {
            path = jsonPath;
            return true;
        }

        int nullSeparator = line.IndexOf('\0', StringComparison.Ordinal);
        if (nullSeparator > 0)
        {
            path = line[..nullSeparator];
            return true;
        }

        int pathSeparator = line.IndexOf(':', StringComparison.Ordinal);
        if (pathSeparator > 0)
        {
            path = line[..pathSeparator];
            return true;
        }

        int contextSeparator = allowContextSeparator ? line.IndexOf('-', StringComparison.Ordinal) : -1;
        if (contextSeparator > 0)
        {
            path = line[..contextSeparator];
            return true;
        }

        return false;
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
