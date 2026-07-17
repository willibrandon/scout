
namespace Scout;

/// <summary>
/// Writes a line while applying match and line highlighting to reportable non-empty spans.
/// </summary>
internal static class ColoredLineWriter
{
    /// <summary>
    /// Writes one line with the requested match ranges and optional whole-line highlighting.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="line">The line bytes to write.</param>
    /// <param name="starts">The zero-based match starts relative to <paramref name="line" />.</param>
    /// <param name="lengths">The match lengths corresponding to <paramref name="starts" />.</param>
    /// <param name="color">The configured output colors.</param>
    /// <param name="maxLength">The maximum number of line bytes to write, or <c>-1</c> for the complete line.</param>
    /// <param name="highlightLine">Whether to apply whole-line highlighting.</param>
    /// <param name="lineTerminator">The optional line-terminator byte excluded from whole-line highlighting.</param>
    public static void Write(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        OutputColor color,
        int maxLength = -1,
        bool highlightLine = false,
        int lineTerminator = -1)
    {
        int limit = maxLength < 0 || maxLength > line.Length ? line.Length : maxLength;
        int rawLimit = limit;
        bool useHighlight = highlightLine && color.HighlightEnabled;
        if (useHighlight && limit > 0 && lineTerminator >= 0 && line[limit - 1] == (byte)lineTerminator)
        {
            limit--;
        }

        if (useHighlight)
        {
            color.WriteHighlightStart(output);
        }

        int position = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            int length = lengths[index];
            if (start >= limit)
            {
                break;
            }

            if (position < start)
            {
                output.Write(line.Slice(position, Math.Min(start, limit) - position));
                position = Math.Min(start, limit);
            }

            int matchEnd = Math.Min(start + length, limit);
            if (matchEnd > start)
            {
                if (useHighlight)
                {
                    color.WriteMatchStart(output);
                    output.Write(line[start..matchEnd]);
                    color.WriteHighlightStart(output);
                }
                else
                {
                    color.WriteMatch(output, line[start..matchEnd]);
                }

                position = matchEnd;
            }
        }

        if (position < limit)
        {
            output.Write(line[position..limit]);
        }

        if (useHighlight)
        {
            color.WriteReset(output);
        }

        if (limit < rawLimit)
        {
            output.Write(line[limit..rawLimit]);
        }
    }
}
