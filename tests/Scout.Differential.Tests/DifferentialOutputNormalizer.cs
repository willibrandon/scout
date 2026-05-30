using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

internal static class DifferentialOutputNormalizer
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] NormalizeStdout(byte[] output, DifferentialComparisonMode comparisonMode)
    {
        if (comparisonMode == DifferentialComparisonMode.Exact)
        {
            return output;
        }

        string text = Utf8.GetString(output);
        if (comparisonMode is DifferentialComparisonMode.MaskElapsed or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            text = MaskElapsed(text);
        }

        if (comparisonMode is DifferentialComparisonMode.SortLines or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            text = SortLines(text);
        }

        return Utf8.GetBytes(text);
    }

    public static string NormalizeStderr(string error, DifferentialComparisonMode comparisonMode)
    {
        if (comparisonMode is DifferentialComparisonMode.MaskElapsed or DifferentialComparisonMode.SortLinesAndMaskElapsed)
        {
            return MaskElapsed(error);
        }

        return error;
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

    private static string SortLines(string text)
    {
        string[] split = text.Split('\n');
        bool hasTrailingLineFeed = split.Length > 0 && split[^1].Length == 0;
        int lineCount = hasTrailingLineFeed ? split.Length - 1 : split.Length;
        var lines = new List<string>(lineCount);
        for (int index = 0; index < lineCount; index++)
        {
            lines.Add(split[index]);
        }

        lines.Sort(StringComparer.Ordinal);
        if (lines.Count == 0)
        {
            return hasTrailingLineFeed ? "\n" : string.Empty;
        }

        string sorted = string.Join('\n', lines);
        return hasTrailingLineFeed ? sorted + "\n" : sorted;
    }
}
