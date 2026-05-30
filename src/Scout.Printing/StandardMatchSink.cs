using System;

namespace Scout;

internal struct StandardMatchSink : IMatchSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> fieldSeparator;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool trim;
    private readonly long lineNumberOffset;
    private readonly long byteOffsetOffset;
    private readonly bool nullPathTerminator;
    private readonly OutputColor color;
    private readonly ReadOnlyMemory<byte> lineTerminator;

    public StandardMatchSink(
        RawByteWriter output,
        OutputPath? prefix,
        ReadOnlyMemory<byte> fieldSeparator,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        long lineNumberOffset = 0,
        long byteOffsetOffset = 0,
        bool nullPathTerminator = false,
        OutputColor color = default,
        ReadOnlyMemory<byte> lineTerminator = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.prefix = prefix;
        this.fieldSeparator = fieldSeparator;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.trim = trim;
        this.lineNumberOffset = lineNumberOffset;
        this.byteOffsetOffset = byteOffsetOffset;
        this.nullPathTerminator = nullPathTerminator;
        this.color = color;
        this.lineTerminator = lineTerminator.IsEmpty ? "\n"u8.ToArray() : lineTerminator;
    }

    public void Matched(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> match)
    {
        ReadOnlySpan<byte> displayMatch = trim ? TrimLeadingAsciiWhitespace(match) : match;
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column;
        bool hasByteOffset = this.byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber + lineNumberOffset, matchColumn);
            color.WritePath(output, prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(nullPathTerminator ? NullByte : fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            color.WriteLineNumber(output, lineNumber + lineNumberOffset);
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
            color.WriteNumberField(output, byteOffset + byteOffsetOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }

        color.WriteMatch(output, displayMatch);
        if (!HasInputTerminator(displayMatch))
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
