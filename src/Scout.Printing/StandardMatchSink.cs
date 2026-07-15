namespace Scout;

/// <summary>
/// Writes individual matches and selection-only lines in standard only-matching format.
/// </summary>
/// <param name="output">The output writer.</param>
/// <param name="prefix">The optional path prefix.</param>
/// <param name="fieldSeparator">The output field separator.</param>
/// <param name="lineNumber">Whether to write line numbers.</param>
/// <param name="column">Whether to write match columns.</param>
/// <param name="byteOffset">Whether to write byte offsets.</param>
/// <param name="trim">Whether to trim leading ASCII whitespace.</param>
/// <param name="lineNumberOffset">The line-number adjustment.</param>
/// <param name="byteOffsetOffset">The byte-offset adjustment.</param>
/// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
/// <param name="color">The configured output colors.</param>
/// <param name="lineTerminator">The output record terminator.</param>
internal struct StandardMatchSink(
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
    ReadOnlyMemory<byte> lineTerminator = default) : IMatchSink, IMatchLineSink
{
    private static readonly byte[] s_nullByte = [0];

    private readonly RawByteWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly OutputPath? _prefix = prefix;
    private readonly ReadOnlyMemory<byte> _fieldSeparator = fieldSeparator;
    private readonly bool _lineNumber = lineNumber;
    private readonly bool _column = column;
    private readonly bool _byteOffset = byteOffset;
    private readonly bool _trim = trim;
    private readonly long _lineNumberOffset = lineNumberOffset;
    private readonly long _byteOffsetOffset = byteOffsetOffset;
    private readonly bool _nullPathTerminator = nullPathTerminator;
    private readonly OutputColor _color = color;
    private readonly ReadOnlyMemory<byte> _lineTerminator = lineTerminator.IsEmpty
        ? "\n"u8.ToArray()
        : lineTerminator;
    private long _lastMatchedLineNumber;

    /// <summary>
    /// Writes one reportable match.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="byteOffset">The zero-based match byte offset.</param>
    /// <param name="matchColumn">The one-based match column.</param>
    /// <param name="match">The matching bytes.</param>
    public void Matched(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> match)
    {
        ReadOnlySpan<byte> displayMatch = _trim ? TrimLeadingAsciiWhitespace(match) : match;
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasColumn = _column && matchColumn > 0;
        bool hasByteOffset = _byteOffset;

        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(
                _output,
                lineNumber + _lineNumberOffset,
                matchColumn > 0 ? matchColumn : null);
            _color.WritePath(_output, _prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_nullPathTerminator ? s_nullByte : _fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            _color.WriteLineNumber(_output, lineNumber + _lineNumberOffset);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasColumn)
        {
            _color.WriteNumberField(_output, matchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            _color.WriteNumberField(_output, byteOffset + _byteOffsetOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }

        if (matchColumn > 0)
        {
            _color.WriteMatch(_output, displayMatch);
        }
        else
        {
            _output.Write(displayMatch);
        }

        if (!HasInputTerminator(displayMatch))
        {
            _output.Write(_lineTerminator.Span);
        }
    }

    /// <summary>
    /// Writes one reportable match with its containing line.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="matchByteOffset">The zero-based match byte offset.</param>
    /// <param name="matchColumn">The one-based match column.</param>
    /// <param name="line">The containing line.</param>
    /// <param name="match">The matching bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        _ = lineByteOffset;
        _ = line;
        _lastMatchedLineNumber = lineNumber;
        Matched(lineNumber, matchByteOffset, matchColumn, match);
    }

    /// <summary>
    /// Writes a selected line when no reportable match was emitted before the line completed.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The completed line.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        if (_lastMatchedLineNumber != lineNumber)
        {
            Matched(lineNumber, lineByteOffset, matchColumn: 0, line);
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
        return _lineTerminator.Length == 1 && _lineTerminator.Span[0] == 0;
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
