namespace Scout;

/// <summary>
/// Buffers colored matched lines and selection-only lines for standard output.
/// </summary>
/// <param name="output">The output writer.</param>
/// <param name="prefix">The optional path prefix.</param>
/// <param name="fieldSeparator">The output field separator.</param>
/// <param name="lineNumber">Whether to write line numbers.</param>
/// <param name="column">Whether to write match columns.</param>
/// <param name="byteOffset">Whether to write byte offsets.</param>
/// <param name="trim">Whether to trim leading ASCII whitespace.</param>
/// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
/// <param name="lineLimit">The output line-length policy.</param>
/// <param name="color">The configured output colors.</param>
/// <param name="lineTerminator">The output record terminator.</param>
/// <param name="lineNumberOffset">The line-number adjustment.</param>
/// <param name="byteOffsetOffset">The byte-offset adjustment.</param>
internal struct ColoredSearchSink(
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
    ReadOnlyMemory<byte> lineTerminator,
    long lineNumberOffset = 0,
    long byteOffsetOffset = 0) : IMatchLineSink
{
    private static readonly byte[] s_nullByte = [0];

    private readonly RawByteWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly OutputPath? _prefix = prefix;
    private readonly ReadOnlyMemory<byte> _fieldSeparator = fieldSeparator;
    private readonly bool _lineNumber = lineNumber;
    private readonly bool _column = column;
    private readonly bool _byteOffset = byteOffset;
    private readonly bool _trim = trim;
    private readonly bool _nullPathTerminator = nullPathTerminator;
    private readonly OutputLineLimit _lineLimit = lineLimit;
    private readonly OutputColor _color = color;
    private readonly ReadOnlyMemory<byte> _lineTerminator = lineTerminator;
    private readonly long _lineNumberOffset = lineNumberOffset;
    private readonly long _byteOffsetOffset = byteOffsetOffset;
    private readonly List<int> _starts = [];
    private readonly List<int> _lengths = [];
    private byte[]? _currentLine;
    private long _currentLineNumber;
    private long _currentLineByteOffset;
    private long _currentMatchColumn;

    /// <summary>
    /// Buffers one reportable match with its containing line.
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
        long adjustedLineNumber = lineNumber + _lineNumberOffset;
        long adjustedLineByteOffset = lineByteOffset + _byteOffsetOffset;
        long adjustedMatchByteOffset = matchByteOffset + _byteOffsetOffset;
        if (_currentLine is not null && _currentLineNumber != adjustedLineNumber)
        {
            Flush();
        }

        if (_currentLine is null)
        {
            _currentLine = line.ToArray();
            _currentLineNumber = adjustedLineNumber;
            _currentLineByteOffset = adjustedLineByteOffset;
            _currentMatchColumn = matchColumn;
        }

        _starts.Add(checked((int)(adjustedMatchByteOffset - adjustedLineByteOffset)));
        _lengths.Add(match.Length);
    }

    /// <summary>
    /// Flushes reportable matches or buffers a selected line when the line completes without one.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The completed line.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        long adjustedLineNumber = lineNumber + _lineNumberOffset;
        if (_currentLine is null)
        {
            _currentLine = line.ToArray();
            _currentLineNumber = adjustedLineNumber;
            _currentLineByteOffset = lineByteOffset + _byteOffsetOffset;
            _currentMatchColumn = 0;
        }

        Flush();
    }

    /// <summary>
    /// Writes and clears the buffered line.
    /// </summary>
    public void Flush()
    {
        if (_currentLine is null)
        {
            return;
        }

        int trimOffset = _trim ? GetTrimOffset(_currentLine) : 0;
        ReadOnlySpan<byte> displayLine = _trim ? _currentLine.AsSpan(trimOffset) : _currentLine;
        WritePrefix();
        if (_lineLimit.IsExceeded(displayLine))
        {
            WriteLimitedBody(displayLine, trimOffset);
        }
        else
        {
            WriteColoredBody(displayLine, trimOffset);
            if (!HasInputTerminator(displayLine))
            {
                _output.Write(_lineTerminator.Span);
            }
        }

        _currentLine = null;
        _starts.Clear();
        _lengths.Clear();
    }

    private void WritePrefix()
    {
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasColumn = _column && _currentMatchColumn > 0;
        bool hasByteOffset = _byteOffset;

        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(
                _output,
                _currentLineNumber,
                _currentMatchColumn > 0 ? _currentMatchColumn : null);
            _color.WritePath(_output, _prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_nullPathTerminator ? s_nullByte : _fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            _color.WriteLineNumber(_output, _currentLineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasColumn)
        {
            _color.WriteNumberField(_output, _currentMatchColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            _color.WriteNumberField(_output, _currentLineByteOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }
    }

    private void WriteLimitedBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        if (_starts.Count == 0)
        {
            if (_lineLimit.Preview)
            {
                _output.Write(displayLine[.._lineLimit.GetPreviewLength(displayLine)]);
                _output.Write(" [... omitted end of long line]"u8);
            }
            else
            {
                _output.Write("[Omitted long matching line]"u8);
            }

            _output.Write(_lineTerminator.Span);
            return;
        }

        if (_lineLimit.Preview)
        {
            int previewLength = _lineLimit.GetPreviewLength(displayLine);
            WriteColoredBody(displayLine[..previewLength], trimOffset);
            _output.Write(" [... "u8);
            long remainingMatches = CountMatchesAfterPreview(
                (ulong)previewLength + (ulong)trimOffset);
            OutputColor.WriteNumber(_output, remainingMatches);
            _output.Write(" more match"u8);
            if (remainingMatches != 1)
            {
                _output.Write("es"u8);
            }

            _output.Write("]"u8);
        }
        else
        {
            _output.Write("[Omitted long line with "u8);
            OutputColor.WriteNumber(_output, _starts.Count);
            _output.Write(" matches]"u8);
        }

        _output.Write(_lineTerminator.Span);
    }

    private void WriteColoredBody(ReadOnlySpan<byte> displayLine, int trimOffset)
    {
        List<int> displayStarts = [];
        List<int> displayLengths = [];
        for (int index = 0; index < _starts.Count; index++)
        {
            int start = _starts[index] - trimOffset;
            int length = _lengths[index];
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

        ColoredLineWriter.Write(
            _output,
            displayLine,
            displayStarts,
            displayLengths,
            _color,
            highlightLine: true,
            lineTerminator: GetLineTerminatorByte());
    }

    private long CountMatchesAfterPreview(ulong originalColumnThreshold)
    {
        long count = 0;
        for (int index = 0; index < _starts.Count; index++)
        {
            if ((ulong)_starts[index] >= originalColumnThreshold)
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
        return _lineTerminator.Length == 1 && _lineTerminator.Span[0] == 0;
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
