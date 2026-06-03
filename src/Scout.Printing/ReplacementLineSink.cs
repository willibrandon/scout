
namespace Scout;

internal struct ReplacementLineSink : IMatchLineSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> fieldSeparator;
    private readonly ReadOnlyMemory<byte> replacement;
    private readonly IReadOnlyList<byte[]> patterns;
    private readonly bool asciiCaseInsensitive;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool trim;
    private readonly bool nullPathTerminator;
    private readonly bool vimgrep;
    private readonly OutputLineLimit lineLimit;
    private readonly long lineNumberOffset;
    private readonly long byteOffsetOffset;
    private readonly ReadOnlyMemory<byte> lineTerminator;
    private readonly List<int> starts;
    private readonly List<int> lengths;
    private readonly List<long> replacementColumns;
    private readonly List<int> replacementLengths;
    private readonly OutputColor color;
    private byte[]? currentLine;
    private long currentLineNumber;
    private long currentLineByteOffset;

    public ReplacementLineSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> fieldSeparator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        OutputLineLimit lineLimit,
        long lineNumberOffset = 0,
        long byteOffsetOffset = 0,
        OutputColor color = default,
        ReadOnlyMemory<byte> lineTerminator = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.prefix = prefix;
        this.fieldSeparator = fieldSeparator;
        this.replacement = replacement;
        this.patterns = patterns;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.trim = trim;
        this.nullPathTerminator = nullPathTerminator;
        this.vimgrep = vimgrep;
        this.lineLimit = lineLimit;
        this.lineNumberOffset = lineNumberOffset;
        this.byteOffsetOffset = byteOffsetOffset;
        this.lineTerminator = lineTerminator.IsEmpty ? "\n"u8.ToArray() : lineTerminator;
        this.color = color;
        starts = [];
        lengths = [];
        replacementColumns = [];
        replacementLengths = [];
        currentLine = null;
        currentLineNumber = 0;
        currentLineByteOffset = 0;
    }

    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _ = matchByteOffset;
        if (currentLine is not null && currentLineNumber != lineNumber)
        {
            Flush();
        }

        if (currentLine is null)
        {
            currentLine = line.ToArray();
            currentLineNumber = lineNumber;
            currentLineByteOffset = lineByteOffset;
        }

        starts.Add((int)matchColumn - 1);
        lengths.Add(match.Length);
    }

    public void Flush()
    {
        if (currentLine is null)
        {
            return;
        }

        byte[] replacedLine = ReplacementFormatter.ReplaceLine(currentLine, starts, lengths, replacement.Span, patterns, asciiCaseInsensitive, replacementColumns, replacementLengths);
        int trimOffset = trim ? GetTrimOffset(replacedLine) : 0;
        ReadOnlySpan<byte> displayLine = replacedLine.AsSpan(trimOffset);
        if (vimgrep)
        {
            for (int index = 0; index < replacementColumns.Count; index++)
            {
                WritePrefix(currentLineNumber + lineNumberOffset, byteOffsetOffset + currentLineByteOffset + replacementColumns[index] - 1, replacementColumns[index]);
                WriteBody(displayLine, trimOffset);
            }
        }
        else
        {
            WritePrefix(currentLineNumber + lineNumberOffset, byteOffsetOffset + currentLineByteOffset, replacementColumns[0]);
            WriteBody(displayLine, trimOffset);
        }

        currentLine = null;
        starts.Clear();
        lengths.Clear();
        replacementColumns.Clear();
        replacementLengths.Clear();
    }

    private void WritePrefix(long outputLineNumber, long outputByteOffset, long outputColumn)
    {
        bool linked = false;
        bool hasLineNumber = lineNumber;
        bool hasColumn = column && outputColumn > 0;
        bool hasByteOffset = byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, outputLineNumber, outputColumn > 0 ? outputColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(nullPathTerminator ? NullByte : fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, outputLineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, outputColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, outputByteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }
    }

    private void WriteBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        if (lineLimit.IsExceeded(displayLine))
        {
            if (lineLimit.Preview)
            {
                output.Write(displayLine[..lineLimit.GetPreviewLength(displayLine)]);
                output.Write(" [... "u8);
                long remaining = CountMatchesAfterPreview();
                OutputColor.WriteNumber(output, remaining);
                output.Write(" more match"u8);
                if (remaining != 1)
                {
                    output.Write("es"u8);
                }

                output.Write("]"u8);
            }
            else
            {
                output.Write("[Omitted long line with "u8);
                OutputColor.WriteNumber(output, replacementColumns.Count);
                output.Write(" matches]"u8);
            }

            output.Write(lineTerminator.Span);
            return;
        }

        if (color.Enabled)
        {
            WriteColoredBody(displayLine, trimOffset);
        }
        else
        {
            output.Write(displayLine);
        }

        if (!HasInputTerminator(displayLine))
        {
            output.Write(lineTerminator.Span);
        }
    }

    private void WriteColoredBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        List<int> displayStarts = [];
        List<int> displayLengths = [];
        for (int index = 0; index < replacementColumns.Count; index++)
        {
            int start = (int)replacementColumns[index] - 1 - trimOffset;
            int length = replacementLengths[index];
            if (start + length <= 0)
            {
                continue;
            }

            if (start < 0)
            {
                length += start;
                start = 0;
            }

            displayStarts.Add(start);
            displayLengths.Add(length);
        }

        ColoredLineWriter.Write(output, displayLine, displayStarts, displayLengths, color);
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

    private long CountMatchesAfterPreview()
    {
        long count = 0;
        ulong columns = lineLimit.MaxColumns.GetValueOrDefault();
        for (int index = 0; index < replacementColumns.Count; index++)
        {
            if ((ulong)replacementColumns[index] > columns)
            {
                count++;
            }
        }

        return count;
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
