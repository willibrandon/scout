
namespace Scout;

internal struct StandardSearchSink : ILineSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> matchSeparator;
    private readonly ReadOnlyMemory<byte> contextSeparator;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool trim;
    private readonly bool nullPathTerminator;
    private readonly OutputLineLimit lineLimit;
    private readonly OutputColor color;
    private readonly ReadOnlyMemory<byte> lineTerminator;
    private readonly long lineNumberOffset;
    private readonly long byteOffsetOffset;
    private readonly bool usePlainLineHeader;
    private byte[]? plainPathPrefix;

    public StandardSearchSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> matchSeparator,
        ReadOnlyMemory<byte> contextSeparator,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        OutputLineLimit lineLimit,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator,
        long lineNumberOffset = 0,
        long byteOffsetOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.prefix = prefix;
        this.matchSeparator = matchSeparator;
        this.contextSeparator = contextSeparator;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.trim = trim;
        this.nullPathTerminator = nullPathTerminator;
        this.lineLimit = lineLimit;
        this.color = color;
        this.lineTerminator = lineTerminator;
        this.lineNumberOffset = lineNumberOffset;
        this.byteOffsetOffset = byteOffsetOffset;
        usePlainLineHeader = CanUsePlainLineHeader(prefix, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color);
        plainPathPrefix = null;
    }

    public ulong MatchedLines { get; private set; }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line)
    {
        MatchedLines++;
        lineNumber += lineNumberOffset;
        byteOffset += byteOffsetOffset;
        if (usePlainLineHeader)
        {
            WritePlainLineHeader(lineNumber);
            output.Write(line);
            if (!HasInputTerminator(line))
            {
                output.Write(lineTerminator.Span);
            }

            return;
        }

        ReadOnlySpan<byte> displayLine = trim ? TrimLeadingAsciiWhitespace(line) : line;
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column && matchColumn > 0;
        bool hasByteOffset = this.byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber, matchColumn > 0 ? matchColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            if (nullPathTerminator)
            {
                output.Write(NullByte);
            }
            else
            {
                output.Write(matchSeparator.Span);
            }
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(matchSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, matchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(matchSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, byteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(matchSeparator.Span);
        }

        WriteBody(displayLine, context: false);
    }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line, int highlightMatchStart, int highlightMatchLength)
    {
        if (!color.Enabled || highlightMatchLength <= 0)
        {
            MatchedLine(lineNumber, byteOffset, matchColumn, line);
            return;
        }

        WriteHighlightedMatchedLine(lineNumber, byteOffset, matchColumn, line, [highlightMatchStart], [highlightMatchLength]);
    }

    public void MatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line, IReadOnlyList<int> highlightStarts, IReadOnlyList<int> highlightLengths)
    {
        if (!color.Enabled || highlightStarts.Count == 0)
        {
            MatchedLine(lineNumber, byteOffset, matchColumn, line);
            return;
        }

        WriteHighlightedMatchedLine(lineNumber, byteOffset, matchColumn, line, highlightStarts, highlightLengths);
    }

    public void ContextLine(long lineNumber, long byteOffset, long contextColumn, ReadOnlySpan<byte> line)
    {
        lineNumber += lineNumberOffset;
        byteOffset += byteOffsetOffset;
        ReadOnlySpan<byte> displayLine = trim ? TrimLeadingAsciiWhitespace(line) : line;
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column && contextColumn > 0;
        bool hasByteOffset = this.byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber, contextColumn > 0 ? contextColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            if (nullPathTerminator)
            {
                output.Write(NullByte);
            }
            else
            {
                output.Write(contextSeparator.Span);
            }
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(contextSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, contextColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(contextSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, byteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(contextSeparator.Span);
        }

        WriteBody(displayLine, context: true);
    }

    private void WriteHighlightedMatchedLine(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> line, IReadOnlyList<int> highlightStarts, IReadOnlyList<int> highlightLengths)
    {
        MatchedLines++;
        lineNumber += lineNumberOffset;
        byteOffset += byteOffsetOffset;
        if (usePlainLineHeader)
        {
            WritePlainLineHeader(lineNumber);
            WriteHighlightedBody(line, highlightStarts, highlightLengths, highlightOffset: 0);
            return;
        }

        int trimOffset = trim ? GetTrimOffset(line) : 0;
        ReadOnlySpan<byte> displayLine = line[trimOffset..];
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column && matchColumn > 0;
        bool hasByteOffset = this.byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber, matchColumn > 0 ? matchColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            if (nullPathTerminator)
            {
                output.Write(NullByte);
            }
            else
            {
                output.Write(matchSeparator.Span);
            }
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(matchSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, matchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(matchSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, byteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(matchSeparator.Span);
        }

        WriteHighlightedBody(displayLine, highlightStarts, highlightLengths, -trimOffset);
    }

    private void WriteBody(ReadOnlySpan<byte> displayLine, bool context)
    {
        if (lineLimit.IsExceeded(displayLine))
        {
            if (lineLimit.Preview)
            {
                output.Write(displayLine[..lineLimit.GetPreviewLength(displayLine)]);
                output.Write(" [... omitted end of long line]"u8);
            }
            else
            {
                output.Write(context ? "[Omitted long context line]"u8 : "[Omitted long matching line]"u8);
            }

            output.Write(lineTerminator.Span);
            return;
        }

        output.Write(displayLine);
        if (!HasInputTerminator(displayLine))
        {
            output.Write(lineTerminator.Span);
        }
    }

    private void WriteHighlightedBody(
        ReadOnlySpan<byte> displayLine,
        IReadOnlyList<int> highlightStarts,
        IReadOnlyList<int> highlightLengths,
        int highlightOffset)
    {
        if (lineLimit.IsExceeded(displayLine))
        {
            if (lineLimit.Preview)
            {
                WriteBodyWithHighlights(displayLine[..lineLimit.GetPreviewLength(displayLine)], highlightStarts, highlightLengths, highlightOffset);
                output.Write(" [... omitted end of long line]"u8);
            }
            else
            {
                output.Write("[Omitted long matching line]"u8);
            }

            output.Write(lineTerminator.Span);
            return;
        }

        WriteBodyWithHighlights(displayLine, highlightStarts, highlightLengths, highlightOffset);
        if (!HasInputTerminator(displayLine))
        {
            output.Write(lineTerminator.Span);
        }
    }

    private void WriteBodyWithHighlights(
        ReadOnlySpan<byte> displayLine,
        IReadOnlyList<int> highlightStarts,
        IReadOnlyList<int> highlightLengths,
        int highlightOffset)
    {
        int contentLength = GetDisplayContentLength(displayLine);
        List<int> displayStarts = [];
        List<int> displayLengths = [];
        for (int index = 0; index < highlightStarts.Count; index++)
        {
            int start = highlightStarts[index] + highlightOffset;
            int length = highlightLengths[index];
            if (length <= 0 ||
                start >= contentLength ||
                start + length <= 0)
            {
                continue;
            }

            if (start < 0)
            {
                length += start;
                start = 0;
            }

            length = Math.Min(length, contentLength - start);
            if (length <= 0)
            {
                continue;
            }

            displayStarts.Add(start);
            displayLengths.Add(length);
        }

        if (displayStarts.Count == 0)
        {
            output.Write(displayLine);
            return;
        }

        ColoredLineWriter.Write(output, displayLine, displayStarts, displayLengths, color);
    }

    private int GetDisplayContentLength(ReadOnlySpan<byte> displayLine)
    {
        if (displayLine.IsEmpty)
        {
            return 0;
        }

        return IsNullLineTerminator()
            ? displayLine[^1] == 0 ? displayLine.Length - 1 : displayLine.Length
            : displayLine[^1] == (byte)'\n' ? displayLine.Length - 1 : displayLine.Length;
    }

    private bool HasInputTerminator(ReadOnlySpan<byte> bytes)
    {
        return !bytes.IsEmpty &&
            (IsNullLineTerminator()
                ? bytes[^1] == 0
                : bytes[^1] == (byte)'\n');
    }

    private bool IsNullLineTerminator()
    {
        return lineTerminator.Length == 1 && lineTerminator.Span[0] == 0;
    }

    private static bool CanUsePlainLineHeader(
        OutputPath? prefix,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        OutputLineLimit lineLimit,
        OutputColor color)
    {
        if (prefix?.HasHyperlink == true ||
            color.Enabled ||
            !lineNumber ||
            column ||
            byteOffset ||
            trim ||
            nullPathTerminator ||
            lineLimit.IsEnabled)
        {
            return false;
        }

        return true;
    }

    private void WritePlainLineHeader(long lineNumber)
    {
        ReadOnlySpan<byte> separator = matchSeparator.Span;
        int digitCount = CountDigits(lineNumber);
        if (prefix is not null)
        {
            plainPathPrefix ??= BuildPlainPathPrefix(prefix.Display.AsSpan(), separator);
            output.Write(plainPathPrefix);
        }

        Span<byte> number = stackalloc byte[digitCount + separator.Length];
        WriteNumber(number[..digitCount], lineNumber);
        separator.CopyTo(number[digitCount..]);
        output.Write(number);
    }

    private static byte[] BuildPlainPathPrefix(ReadOnlySpan<byte> path, ReadOnlySpan<byte> separator)
    {
        byte[] result = new byte[path.Length + separator.Length];
        path.CopyTo(result);
        separator.CopyTo(result.AsSpan(path.Length));
        return result;
    }

    private static int CountDigits(long value)
    {
        ulong number = (ulong)value;
        int digits = 1;
        while (number >= 10)
        {
            number /= 10;
            digits++;
        }

        return digits;
    }

    private static void WriteNumber(Span<byte> destination, long value)
    {
        ulong number = (ulong)value;
        int index = destination.Length;
        do
        {
            index--;
            destination[index] = (byte)((number % 10) + (byte)'0');
            number /= 10;
        }
        while (number != 0);
    }

    private static ReadOnlySpan<byte> TrimLeadingAsciiWhitespace(ReadOnlySpan<byte> bytes)
    {
        return bytes[GetTrimOffset(bytes)..];
    }

    private static int GetTrimOffset(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length && IsAsciiWhitespace(bytes[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
    }

}
