
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
    private byte[]? plainLinePrefix;
    private byte[]? plainLineHeaderBuffer;

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
        plainLinePrefix = null;
        plainLineHeaderBuffer = null;
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

    private static byte[] CreatePlainLinePrefix(OutputPath prefix, ReadOnlyMemory<byte> matchSeparator)
    {
        byte[] bytes = new byte[prefix.Display.Length + matchSeparator.Length];
        prefix.Display.CopyTo(bytes, 0);
        matchSeparator.CopyTo(bytes.AsMemory(prefix.Display.Length));
        return bytes;
    }

    private void WritePlainLineHeader(long lineNumber)
    {
        ReadOnlySpan<byte> separator = matchSeparator.Span;
        plainLinePrefix ??= prefix is null ? [] : CreatePlainLinePrefix(prefix, matchSeparator);
        int digitCount = CountDigits(lineNumber);
        int headerLength = plainLinePrefix!.Length + digitCount + separator.Length;
        byte[] header = plainLineHeaderBuffer ??= new byte[plainLinePrefix.Length + 20 + separator.Length];
        plainLinePrefix.CopyTo(header);
        int numberStart = plainLinePrefix.Length;
        WriteNumber(header.AsSpan(numberStart, digitCount), lineNumber);
        separator.CopyTo(header.AsSpan(numberStart + digitCount));
        output.Write(header.AsSpan(0, headerLength));
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
        int index = 0;
        while (index < bytes.Length && IsAsciiWhitespace(bytes[index]))
        {
            index++;
        }

        return bytes[index..];
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
    }

}
