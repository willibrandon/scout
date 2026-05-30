using System;
using System.Collections.Generic;

namespace Scout;

internal struct ColoredSearchSink : IMatchLineSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> fieldSeparator;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool trim;
    private readonly bool nullPathTerminator;
    private readonly OutputLineLimit lineLimit;
    private readonly OutputColor color;
    private readonly ReadOnlyMemory<byte> lineTerminator;
    private readonly List<int> starts;
    private readonly List<int> lengths;
    private byte[]? currentLine;
    private long currentLineNumber;
    private long currentLineByteOffset;
    private long currentMatchColumn;

    public ColoredSearchSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> fieldSeparator,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        OutputLineLimit lineLimit,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.prefix = prefix;
        this.fieldSeparator = fieldSeparator;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.trim = trim;
        this.nullPathTerminator = nullPathTerminator;
        this.lineLimit = lineLimit;
        this.color = color;
        this.lineTerminator = lineTerminator;
        starts = [];
        lengths = [];
        currentLine = null;
        currentLineNumber = 0;
        currentLineByteOffset = 0;
        currentMatchColumn = 0;
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
            currentMatchColumn = matchColumn;
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

        int trimOffset = GetTrimOffset(currentLine);
        ReadOnlySpan<byte> displayLine = trim ? currentLine.AsSpan(trimOffset) : currentLine;
        WritePrefix();
        if (lineLimit.IsExceeded(displayLine))
        {
            WriteLimitedBody(displayLine, trimOffset);
        }
        else
        {
            WriteColoredBody(displayLine, trimOffset);
            if (!HasInputTerminator(displayLine))
            {
                output.Write(lineTerminator.Span);
            }
        }

        currentLine = null;
        starts.Clear();
        lengths.Clear();
    }

    private void WritePrefix()
    {
        bool linked = false;
        bool hasLineNumber = lineNumber;
        bool hasColumn = column && currentMatchColumn > 0;
        bool hasByteOffset = byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, currentLineNumber, currentMatchColumn > 0 ? currentMatchColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(nullPathTerminator ? NullByte : fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, currentLineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, currentMatchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, currentLineByteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }
    }

    private void WriteLimitedBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        if (lineLimit.Preview)
        {
            int previewLength = lineLimit.GetPreviewLength(displayLine);
            WriteColoredBody(displayLine[..previewLength], trimOffset);
            output.Write(" [... "u8);
            OutputColor.WriteNumber(output, CountMatchesAfterPreview((ulong)previewLength + (ulong)trimOffset));
            output.Write(" more match"u8);
            if (CountMatchesAfterPreview((ulong)previewLength + (ulong)trimOffset) != 1)
            {
                output.Write("es"u8);
            }

            output.Write("]"u8);
        }
        else
        {
            output.Write("[Omitted long line with "u8);
            OutputColor.WriteNumber(output, starts.Count);
            output.Write(" matches]"u8);
        }

        output.Write(lineTerminator.Span);
    }

    private void WriteColoredBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        List<int> displayStarts = [];
        List<int> displayLengths = [];
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index] - trimOffset;
            int length = lengths[index];
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

        ColoredLineWriter.Write(output, displayLine, displayStarts, displayLengths, color, highlightLine: true, lineTerminator: GetLineTerminatorByte());
    }

    private long CountMatchesAfterPreview(ulong originalColumnThreshold)
    {
        long count = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            if ((ulong)starts[index] >= originalColumnThreshold)
            {
                count++;
            }
        }

        return count;
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

    private int GetLineTerminatorByte()
    {
        return IsNullLineTerminator() ? 0 : (byte)'\n';
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
