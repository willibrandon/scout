
namespace Scout;

internal struct VimgrepSink : IMatchLineSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> fieldSeparator;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool onlyMatching;
    private readonly bool trim;
    private readonly bool nullPathTerminator;
    private readonly OutputLineLimit lineLimit;
    private readonly IReadOnlyList<byte[]> pattern;
    private readonly bool asciiCaseInsensitive;
    private readonly bool lineRegexp;
    private readonly bool wordRegexp;
    private readonly bool crlf;
    private readonly bool nullData;
    private readonly OutputColor color;
    private readonly ReadOnlyMemory<byte> lineTerminator;

    public VimgrepSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> fieldSeparator,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool onlyMatching,
        bool trim,
        bool nullPathTerminator,
        OutputLineLimit lineLimit,
        IReadOnlyList<byte[]> pattern,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool crlf,
        bool nullData,
        OutputColor color,
        ReadOnlyMemory<byte> lineTerminator)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pattern);
        this.output = output;
        this.prefix = prefix;
        this.fieldSeparator = fieldSeparator;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.onlyMatching = onlyMatching;
        this.trim = trim;
        this.nullPathTerminator = nullPathTerminator;
        this.lineLimit = lineLimit;
        this.pattern = pattern;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.lineRegexp = lineRegexp;
        this.wordRegexp = wordRegexp;
        this.crlf = crlf;
        this.nullData = nullData;
        this.color = color;
        this.lineTerminator = lineTerminator;
    }

    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _ = lineByteOffset;
        ReadOnlySpan<byte> body = onlyMatching ? match : line;
        int trimOffset = trim ? GetTrimOffset(body) : 0;
        ReadOnlySpan<byte> displayBody = body[trimOffset..];
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column && matchColumn > 0;
        bool hasByteOffset = byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber, matchColumn > 0 ? matchColumn : null);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(nullPathTerminator ? NullByte : fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasColumn)
        {
            color.WriteNumberField(output, matchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, matchByteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }

        WriteBody(displayBody, line, matchColumn - 1 - trimOffset, match.Length);
    }

    private void WriteBody(ReadOnlySpan<byte> displayBody, ReadOnlySpan<byte> line, long matchStart, int matchLength)
    {
        if (!onlyMatching && lineLimit.IsExceeded(displayBody))
        {
            if (lineLimit.Preview)
            {
                ulong columns = lineLimit.MaxColumns.GetValueOrDefault();
                long remainingMatches = LiteralLineSearcher.CountLineMatchesAfterColumn(line, pattern, columns, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData);
                WriteHighlightedBody(displayBody[..lineLimit.GetPreviewLength(displayBody)], matchStart, matchLength);
                output.Write(" [... "u8);
                OutputColor.WriteNumber(output, remainingMatches);
                output.Write(" more match"u8);
                if (remainingMatches != 1)
                {
                    output.Write("es"u8);
                }

                output.Write("]"u8);
            }
            else
            {
                output.Write("[Omitted long line with "u8);
                OutputColor.WriteNumber(output, LiteralLineSearcher.CountLineMatches(line, pattern, asciiCaseInsensitive, lineRegexp, wordRegexp, crlf, nullData));
                output.Write(" matches]"u8);
            }

            output.Write(lineTerminator.Span);
            return;
        }

        if (onlyMatching)
        {
            color.WriteMatch(output, displayBody);
        }
        else
        {
            WriteHighlightedBody(displayBody, matchStart, matchLength);
        }

        if (!HasInputTerminator(displayBody))
        {
            output.Write(lineTerminator.Span);
        }
    }

    private void WriteHighlightedBody(ReadOnlySpan<byte> displayBody, long matchStart, int matchLength)
    {
        if (!color.Enabled || matchStart < 0 || matchStart >= displayBody.Length)
        {
            output.Write(displayBody);
            return;
        }

        int start = (int)matchStart;
        int length = Math.Min(matchLength, displayBody.Length - start);
        ColoredLineWriter.Write(output, displayBody, [start], [length], color);
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
