namespace Scout;

/// <summary>
/// Writes vimgrep records for reportable matches and selection-only lines.
/// </summary>
/// <param name="output">The output writer.</param>
/// <param name="prefix">The optional path prefix.</param>
/// <param name="fieldSeparator">The output field separator.</param>
/// <param name="lineNumber">Whether to write line numbers.</param>
/// <param name="column">Whether to write match columns.</param>
/// <param name="byteOffset">Whether to write byte offsets.</param>
/// <param name="onlyMatching">Whether to write only matching bytes.</param>
/// <param name="trim">Whether to trim leading ASCII whitespace.</param>
/// <param name="nullPathTerminator">Whether path prefixes use a NUL terminator.</param>
/// <param name="lineLimit">The output line-length policy.</param>
/// <param name="color">The configured output colors.</param>
/// <param name="lineTerminator">The output record terminator.</param>
/// <param name="lineNumberOffset">The line-number adjustment.</param>
/// <param name="byteOffsetOffset">The byte-offset adjustment.</param>
internal struct VimgrepSink(
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
    private readonly bool _onlyMatching = onlyMatching;
    private readonly bool _trim = trim;
    private readonly bool _nullPathTerminator = nullPathTerminator;
    private readonly OutputLineLimit _lineLimit = lineLimit;
    private readonly OutputColor _color = color;
    private readonly ReadOnlyMemory<byte> _lineTerminator = lineTerminator;
    private readonly long _lineNumberOffset = lineNumberOffset;
    private readonly long _byteOffsetOffset = byteOffsetOffset;
    private List<int>? _deferredMatchStarts;
    private List<long>? _deferredMatchColumns;
    private List<int>? _deferredMatchLengths;
    private long _lastMatchedLineNumber;

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
        _lastMatchedLineNumber = lineNumber;
        if (ShouldDeferLine(line))
        {
            (_deferredMatchStarts ??= []).Add(
                checked((int)(matchByteOffset - lineByteOffset)));
            (_deferredMatchColumns ??= []).Add(matchColumn);
            (_deferredMatchLengths ??= []).Add(match.Length);
            return;
        }

        WriteMatchedLine(
            lineNumber,
            matchByteOffset,
            matchColumn,
            line,
            match,
            lineMatchCount: 0,
            matchesAfterPreview: 0);
    }

    private void WriteMatchedLine(
        long lineNumber,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match,
        long lineMatchCount,
        long matchesAfterPreview)
    {
        lineNumber += _lineNumberOffset;
        matchByteOffset += _byteOffsetOffset;
        ReadOnlySpan<byte> body = _onlyMatching && matchColumn > 0 ? match : line;
        int trimOffset = _trim ? GetTrimOffset(body) : 0;
        ReadOnlySpan<byte> displayBody = body[trimOffset..];
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasColumn = _column && matchColumn > 0;
        bool hasByteOffset = _byteOffset;

        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(_output, lineNumber, matchColumn > 0 ? matchColumn : null);
            _color.WritePath(_output, _prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_nullPathTerminator ? s_nullByte : _fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            _color.WriteLineNumber(_output, lineNumber);
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
            _color.WriteNumberField(_output, matchByteOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }

        WriteBody(
            displayBody,
            matchColumn - 1 - trimOffset,
            match.Length,
            lineMatchCount,
            matchesAfterPreview);
    }

    /// <summary>
    /// Writes a selected line when no reportable match was emitted before the line completed.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The completed line.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        if (_deferredMatchStarts is { Count: > 0 } matchStarts)
        {
            List<long> matchColumns = _deferredMatchColumns!;
            List<int> matchLengths = _deferredMatchLengths!;
            int trimOffset = _trim ? GetTrimOffset(line) : 0;
            int previewLength = _lineLimit.GetPreviewLength(line[trimOffset..]);
            long matchesAfterPreview = CountDeferredMatchesAtOrAfter(previewLength);
            long lineMatchCount = matchStarts.Count;
            for (int index = 0; index < matchStarts.Count; index++)
            {
                int matchStart = matchStarts[index];
                int matchLength = matchLengths[index];
                WriteMatchedLine(
                    lineNumber,
                    lineByteOffset + matchStart,
                    matchColumns[index],
                    line,
                    line.Slice(matchStart, matchLength),
                    lineMatchCount,
                    matchesAfterPreview);
            }

            matchStarts.Clear();
            matchColumns.Clear();
            matchLengths.Clear();
            return;
        }

        if (_lastMatchedLineNumber != lineNumber)
        {
            WriteMatchedLine(
                lineNumber,
                lineByteOffset,
                matchColumn: 0,
                line,
                line,
                lineMatchCount: 0,
                matchesAfterPreview: 0);
        }
    }

    private void WriteBody(
        ReadOnlySpan<byte> displayBody,
        long matchStart,
        int matchLength,
        long lineMatchCount,
        long matchesAfterPreview)
    {
        if ((_onlyMatching || matchStart < 0) &&
            _lineLimit.IsExceeded(displayBody))
        {
            if (_lineLimit.Preview)
            {
                _output.Write(displayBody[.._lineLimit.GetPreviewLength(displayBody)]);
                _output.Write(" [... omitted end of long line]"u8);
            }
            else
            {
                _output.Write("[Omitted long matching line]"u8);
            }

            _output.Write(_lineTerminator.Span);
            return;
        }

        if (!_onlyMatching && _lineLimit.IsExceeded(displayBody))
        {
            if (_lineLimit.Preview)
            {
                WriteHighlightedBody(
                    displayBody[.._lineLimit.GetPreviewLength(displayBody)],
                    matchStart,
                    matchLength);
                _output.Write(" [... "u8);
                OutputColor.WriteNumber(_output, matchesAfterPreview);
                _output.Write(" more match"u8);
                if (matchesAfterPreview != 1)
                {
                    _output.Write("es"u8);
                }

                _output.Write("]"u8);
            }
            else
            {
                _output.Write("[Omitted long line with "u8);
                OutputColor.WriteNumber(_output, lineMatchCount);
                _output.Write(" matches]"u8);
            }

            _output.Write(_lineTerminator.Span);
            return;
        }

        if (_onlyMatching && matchStart >= 0)
        {
            _color.WriteMatch(_output, displayBody);
        }
        else
        {
            WriteHighlightedBody(displayBody, matchStart, matchLength);
        }

        if (!HasInputTerminator(displayBody))
        {
            _output.Write(_lineTerminator.Span);
        }
    }

    private bool ShouldDeferLine(ReadOnlySpan<byte> line)
    {
        if (_deferredMatchStarts is { Count: > 0 })
        {
            return true;
        }

        int trimOffset = _trim ? GetTrimOffset(line) : 0;
        return _lineLimit.IsExceeded(line[trimOffset..]);
    }

    private long CountDeferredMatchesAtOrAfter(int start)
    {
        long count = 0;
        List<int> matchStarts = _deferredMatchStarts!;
        for (int index = 0; index < matchStarts.Count; index++)
        {
            if (matchStarts[index] >= start)
            {
                count++;
            }
        }

        return count;
    }

    private void WriteHighlightedBody(ReadOnlySpan<byte> displayBody, long matchStart, int matchLength)
    {
        if (!_color.Enabled || matchStart < 0 || matchStart >= displayBody.Length)
        {
            _output.Write(displayBody);
            return;
        }

        int start = (int)matchStart;
        int length = Math.Min(matchLength, displayBody.Length - start);
        ColoredLineWriter.Write(_output, displayBody, [start], [length], _color);
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
