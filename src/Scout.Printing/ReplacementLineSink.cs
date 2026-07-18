namespace Scout;

/// <summary>
/// Replaces every match in a line while preserving the original line context for capture replay.
/// </summary>
/// <param name="output">The output writer.</param>
/// <param name="prefix">The optional path prefix.</param>
/// <param name="fieldSeparator">The separator written between output fields.</param>
/// <param name="replacement">The replacement template.</param>
/// <param name="lineNumber">Whether to write line numbers.</param>
/// <param name="column">Whether to write columns.</param>
/// <param name="byteOffset">Whether to write byte offsets.</param>
/// <param name="trim">Whether to trim leading ASCII whitespace.</param>
/// <param name="nullPathTerminator">Whether to terminate path fields with a NUL byte.</param>
/// <param name="vimgrep">Whether to emit one record per replacement.</param>
/// <param name="lineLimit">The output line-length policy.</param>
/// <param name="lineNumberOffset">The line-number offset applied to emitted matches.</param>
/// <param name="byteOffsetOffset">The byte-offset adjustment applied to emitted matches.</param>
/// <param name="color">The configured output colors.</param>
/// <param name="lineTerminator">The output line terminator.</param>
/// <param name="searchPlan">The optional authoritative regex search plan.</param>
/// <param name="captureProvider">An optional non-native authoritative capture provider.</param>
/// <param name="streamPlainBodyDirectly">Whether plain replacement output may be streamed directly.</param>
internal struct ReplacementLineSink(
    RawByteWriter output,
    OutputPath? prefix,
    ReadOnlyMemory<byte> fieldSeparator,
    ReadOnlyMemory<byte> replacement,
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
    ReadOnlyMemory<byte> lineTerminator = default,
    RegexSearchPlan? searchPlan = null,
    IReplacementCaptureProvider? captureProvider = null,
    bool streamPlainBodyDirectly = false) : IMatchLineSink, IDisposable
{
    private static readonly byte[] s_nullByte = [0];
    private static readonly byte[] s_lineFeed = [(byte)'\n'];

    private readonly RawByteWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly OutputPath? _prefix = prefix;
    private readonly ReadOnlyMemory<byte> _fieldSeparator = fieldSeparator;
    private readonly ReadOnlyMemory<byte> _replacement = replacement;
    private readonly RegexSearchPlan? _searchPlan = searchPlan;
    private readonly IReplacementCaptureProvider? _externalCaptureProvider = captureProvider;
    private RegexCaptureReplaySession? _captureSession;
    private ReplacementTemplate? _template;
    private int[]? _captureSlotsBuffer;
    private readonly bool _streamPlainBodyDirectly = streamPlainBodyDirectly;
    private readonly bool _lineNumber = lineNumber;
    private readonly bool _column = column;
    private readonly bool _byteOffset = byteOffset;
    private readonly bool _trim = trim;
    private readonly bool _nullPathTerminator = nullPathTerminator;
    private readonly bool _vimgrep = vimgrep;
    private readonly OutputLineLimit _lineLimit = lineLimit;
    private readonly long _lineNumberOffset = lineNumberOffset;
    private readonly long _byteOffsetOffset = byteOffsetOffset;
    private readonly ReadOnlyMemory<byte> _lineTerminator =
        lineTerminator.IsEmpty ? s_lineFeed : lineTerminator;
    private readonly OutputColor _color = color;
    private ReplacementLineAccumulator? _accumulator;
    private byte[]? _currentLine;
    private long _currentLineNumber;
    private long _currentLineByteOffset;
    private int _currentLineWrittenUntil;
    private bool _streamingPlainLine;
    private bool _selectionOnly;

    /// <summary>
    /// Gets a value indicating whether buffered-line accumulator storage has been initialized.
    /// </summary>
    internal readonly bool IsAccumulatorInitialized => _accumulator is not null;

    /// <summary>
    /// Records one match and its original containing-line context.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="line">The containing line bytes.</param>
    /// <param name="match">The matching bytes.</param>
    public void MatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match)
    {
        RecordMatchedLine(
            lineNumber,
            lineByteOffset,
            matchByteOffset,
            matchColumn,
            line,
            match,
            checked((int)matchColumn - 1));
    }

    /// <summary>
    /// Records one match together with the start of its successful engine search.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="matchByteOffset">The zero-based byte offset of the match.</param>
    /// <param name="matchColumn">The one-based byte column of the match.</param>
    /// <param name="line">The containing line bytes.</param>
    /// <param name="match">The matching bytes.</param>
    /// <param name="searchStart">The zero-based start of the successful engine search in <paramref name="line" />.</param>
    internal void MatchedLineWithSearchStart(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match,
        int searchStart)
    {
        RecordMatchedLine(
            lineNumber,
            lineByteOffset,
            matchByteOffset,
            matchColumn,
            line,
            match,
            searchStart);
    }

    private void RecordMatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match,
        int searchStart)
    {
        _ = matchByteOffset;
        if (_currentLine is not null && _currentLineNumber != lineNumber)
        {
            Flush();
        }

        if (_streamingPlainLine && _currentLineNumber != lineNumber)
        {
            ResetLineState();
        }

        _selectionOnly = false;

        if (_streamPlainBodyDirectly && CanWritePlainBodyDirectly())
        {
            StreamPlainMatchedLine(lineNumber, lineByteOffset, matchColumn, line, match, searchStart);
            return;
        }

        if (_currentLine is null)
        {
            _currentLine = line.ToArray();
            _currentLineNumber = lineNumber;
            _currentLineByteOffset = lineByteOffset;
        }

        ReplacementLineAccumulator accumulator = GetAccumulator();
        accumulator.Starts.Add((int)matchColumn - 1);
        accumulator.Lengths.Add(match.Length);
        accumulator.SearchStarts.Add(searchStart);
    }

    /// <summary>
    /// Completes a matching line and writes any buffered replacement output.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based byte offset of the line.</param>
    /// <param name="line">The containing line bytes.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        if (_streamingPlainLine && _currentLineNumber == lineNumber)
        {
            _output.Write(line[_currentLineWrittenUntil..]);
            if (!HasInputTerminator(line))
            {
                _output.Write(_lineTerminator.Span);
            }

            ResetLineState();
            return;
        }

        if (_currentLine is null)
        {
            _currentLine = line.ToArray();
            _currentLineNumber = lineNumber;
            _currentLineByteOffset = lineByteOffset;
            _selectionOnly = true;
        }

        if (_currentLine is not null && _currentLineNumber == lineNumber)
        {
            Flush();
        }
    }

    /// <summary>
    /// Writes and clears any buffered replacement line.
    /// </summary>
    public void Flush()
    {
        if (_streamingPlainLine)
        {
            ResetLineState();
            return;
        }

        if (_currentLine is null)
        {
            return;
        }

        if (_selectionOnly)
        {
            WritePrefix(
                _currentLineNumber + _lineNumberOffset,
                _byteOffsetOffset + _currentLineByteOffset,
                outputColumn: 0);
            int selectedTrimOffset = _trim ? GetTrimOffset(_currentLine) : 0;
            ReadOnlySpan<byte> selectedDisplayLine = _currentLine.AsSpan(selectedTrimOffset);
            WriteSelectedBody(selectedDisplayLine);

            ResetLineState();
            return;
        }

        if (CanWritePlainBodyDirectly())
        {
            ReplacementLineAccumulator accumulator = GetAccumulator();
            WritePrefix(
                _currentLineNumber + _lineNumberOffset,
                _byteOffsetOffset + _currentLineByteOffset,
                accumulator.Starts.Count > 0 ? accumulator.Starts[0] + 1L : 1L);
            ReplacementTemplate activeTemplate = GetTemplate();
            WriteReplacedLine(activeTemplate);
            if (!HasInputTerminator(_currentLine))
            {
                _output.Write(_lineTerminator.Span);
            }

            ResetLineState();
            return;
        }

        ReplacementTemplate replacementTemplate = GetTemplate();
        byte[] replacedLine = ReplaceLine(replacementTemplate);
        int trimOffset = _trim ? GetTrimOffset(replacedLine) : 0;
        ReadOnlySpan<byte> displayLine = replacedLine.AsSpan(trimOffset);
        ReplacementLineAccumulator activeAccumulator = GetAccumulator();
        if (_vimgrep)
        {
            for (int index = 0; index < activeAccumulator.ReplacementColumns.Count; index++)
            {
                WritePrefix(
                    _currentLineNumber + _lineNumberOffset,
                    _byteOffsetOffset + _currentLineByteOffset + activeAccumulator.ReplacementColumns[index] - 1,
                    activeAccumulator.ReplacementColumns[index]);
                WriteBody(displayLine, trimOffset, index);
            }
        }
        else
        {
            WritePrefix(
                _currentLineNumber + _lineNumberOffset,
                _byteOffsetOffset + _currentLineByteOffset,
                activeAccumulator.ReplacementColumns[0]);
            WriteBody(displayLine, trimOffset);
        }

        ResetLineState();
    }

    private bool CanWritePlainBodyDirectly()
    {
        return !_trim &&
            !_vimgrep &&
            !_color.Enabled &&
            !_lineLimit.IsEnabled;
    }

    private void WriteSelectedBody(ReadOnlySpan<byte> displayLine)
    {
        if (_lineLimit.IsExceeded(displayLine))
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

        _output.Write(displayLine);
        if (!HasInputTerminator(displayLine))
        {
            _output.Write(_lineTerminator.Span);
        }
    }

    private void StreamPlainMatchedLine(
        long lineNumber,
        long lineByteOffset,
        long matchColumn,
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> match,
        int searchStart)
    {
        int matchStart = checked((int)matchColumn - 1);
        if (!_streamingPlainLine)
        {
            _currentLineNumber = lineNumber;
            _currentLineByteOffset = lineByteOffset;
            _currentLineWrittenUntil = 0;
            _streamingPlainLine = true;
            WritePrefix(
                _currentLineNumber + _lineNumberOffset,
                _byteOffsetOffset + _currentLineByteOffset,
                matchColumn);
        }

        if ((uint)matchStart > (uint)line.Length ||
            match.Length > line.Length - matchStart ||
            matchStart < _currentLineWrittenUntil)
        {
            return;
        }

        int gapLength = matchStart - _currentLineWrittenUntil;
        if (gapLength > 0)
        {
            _output.Write(line.Slice(_currentLineWrittenUntil, gapLength));
        }

        ReplacementTemplate activeTemplate = GetTemplate();
        IReplacementCaptureProvider? captureProvider = GetCaptureProvider(activeTemplate);
        if (captureProvider is null)
        {
            ReplacementFormatter.WriteExpanded(
                _output,
                _replacement.Span,
                line,
                matchStart,
                match.Length,
                searchPlan: null,
                activeTemplate,
                GetCaptureSlotsBuffer(activeTemplate));
        }
        else
        {
            ReplacementFormatter.WriteExpanded(
                _output,
                _replacement.Span,
                line,
                matchStart,
                match.Length,
                captureProvider,
                searchStart,
                activeTemplate,
                GetCaptureSlotsBuffer(activeTemplate));
        }

        _currentLineWrittenUntil = matchStart + match.Length;
    }

    private byte[] ReplaceLine(ReplacementTemplate activeTemplate)
    {
        ReplacementLineAccumulator accumulator = GetAccumulator();
        IReplacementCaptureProvider? captureProvider = GetCaptureProvider(activeTemplate);
        if (captureProvider is null)
        {
            return ReplacementFormatter.ReplaceLine(
                _currentLine,
                accumulator.Starts,
                accumulator.Lengths,
                _replacement.Span,
                accumulator.ReplacementColumns,
                accumulator.ReplacementLengths,
                searchPlan: null,
                activeTemplate,
                GetCaptureSlotsBuffer(activeTemplate));
        }

        return ReplacementFormatter.ReplaceLine(
            _currentLine,
            accumulator.Starts,
            accumulator.Lengths,
            _replacement.Span,
            accumulator.ReplacementColumns,
            accumulator.ReplacementLengths,
            captureProvider,
            accumulator.SearchStarts,
            activeTemplate,
            GetCaptureSlotsBuffer(activeTemplate));
    }

    private void WriteReplacedLine(ReplacementTemplate activeTemplate)
    {
        ReplacementLineAccumulator accumulator = GetAccumulator();
        IReplacementCaptureProvider? captureProvider = GetCaptureProvider(activeTemplate);
        if (captureProvider is null)
        {
            ReplacementFormatter.WriteReplacedLine(
                _output,
                _currentLine,
                accumulator.Starts,
                accumulator.Lengths,
                _replacement.Span,
                searchPlan: null,
                activeTemplate,
                GetCaptureSlotsBuffer(activeTemplate));
            return;
        }

        ReplacementFormatter.WriteReplacedLine(
            _output,
            _currentLine,
            accumulator.Starts,
            accumulator.Lengths,
            _replacement.Span,
            captureProvider,
            accumulator.SearchStarts,
            activeTemplate,
            GetCaptureSlotsBuffer(activeTemplate));
    }

    private ReplacementTemplate GetTemplate()
    {
        return _template ??= ReplacementTemplate.Create(_replacement.Span);
    }

    private int[] GetCaptureSlotsBuffer(ReplacementTemplate activeTemplate)
    {
        int captureCount = activeTemplate.RequiresSubcaptures
            ? Math.Max(activeTemplate.HighestCapture, GetCaptureCount())
            : 0;
        return _captureSlotsBuffer ??= new int[checked(2 * (captureCount + 1))];
    }

    private IReplacementCaptureProvider? GetCaptureProvider(ReplacementTemplate activeTemplate)
    {
        if (!activeTemplate.RequiresSubcaptures)
        {
            return null;
        }

        if (_externalCaptureProvider is not null)
        {
            return _externalCaptureProvider;
        }

        return _searchPlan is null
            ? null
            : _captureSession ??= new RegexCaptureReplaySession(_searchPlan);
    }

    private int GetCaptureCount()
    {
        return _externalCaptureProvider?.CaptureCount ?? _searchPlan?.CaptureCount ?? 0;
    }

    /// <summary>
    /// Returns the operation-scoped capture runner when this sink used native subcaptures.
    /// </summary>
    public void Dispose()
    {
        _captureSession?.Dispose();
        _captureSession = null;
    }

    private void WritePrefix(long outputLineNumber, long outputByteOffset, long outputColumn)
    {
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasColumn = _column && outputColumn > 0;
        bool hasByteOffset = _byteOffset;

        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(
                _output,
                outputLineNumber,
                outputColumn > 0 ? outputColumn : null);
            _color.WritePath(_output, _prefix.Display);
            if (!hasLineNumber && !hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_nullPathTerminator ? s_nullByte : _fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            _color.WriteLineNumber(_output, outputLineNumber);
            if (!hasColumn && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasColumn)
        {
            _color.WriteNumberField(_output, outputColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            _color.WriteNumberField(_output, outputByteOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }
    }

    private void WriteBody(ReadOnlySpan<byte> displayLine, int trimOffset, int highlightedReplacementIndex = -1)
    {
        if (_lineLimit.IsExceeded(displayLine))
        {
            if (_lineLimit.Preview)
            {
                WriteBodyWithOptionalColor(
                    displayLine[.._lineLimit.GetPreviewLength(displayLine)],
                    trimOffset,
                    highlightedReplacementIndex);
                _output.Write(" [... "u8);
                long remaining = CountMatchesAfterPreview();
                OutputColor.WriteNumber(_output, remaining);
                _output.Write(" more match"u8);
                if (remaining != 1)
                {
                    _output.Write("es"u8);
                }

                _output.Write("]"u8);
            }
            else
            {
                _output.Write("[Omitted long line with "u8);
                OutputColor.WriteNumber(
                    _output,
                    GetAccumulator().ReplacementColumns.Count);
                _output.Write(" matches]"u8);
            }

            _output.Write(_lineTerminator.Span);
            return;
        }

        if (_color.Enabled)
        {
            WriteColoredBody(displayLine, trimOffset, highlightedReplacementIndex);
        }
        else
        {
            _output.Write(displayLine);
        }

        if (!HasInputTerminator(displayLine))
        {
            _output.Write(_lineTerminator.Span);
        }
    }

    private void WriteBodyWithOptionalColor(ReadOnlySpan<byte> displayLine, int trimOffset, int highlightedReplacementIndex)
    {
        if (_color.Enabled)
        {
            WriteColoredBody(displayLine, trimOffset, highlightedReplacementIndex);
        }
        else
        {
            _output.Write(displayLine);
        }
    }

    private void WriteColoredBody(ReadOnlySpan<byte> displayLine, int trimOffset, int highlightedReplacementIndex)
    {
        ReplacementLineAccumulator accumulator = GetAccumulator();
        List<int> displayStarts = [];
        List<int> displayLengths = [];
        int startIndex = highlightedReplacementIndex >= 0 ? highlightedReplacementIndex : 0;
        int endIndex = highlightedReplacementIndex >= 0
            ? highlightedReplacementIndex + 1
            : accumulator.ReplacementColumns.Count;
        for (int index = startIndex; index < endIndex; index++)
        {
            int start = (int)accumulator.ReplacementColumns[index] - 1 - trimOffset;
            int length = accumulator.ReplacementLengths[index];
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

        ColoredLineWriter.Write(_output, displayLine, displayStarts, displayLengths, _color);
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

    private long CountMatchesAfterPreview()
    {
        ReplacementLineAccumulator accumulator = GetAccumulator();
        long count = 0;
        ulong columns = _lineLimit.MaxColumns.GetValueOrDefault();
        for (int index = 0; index < accumulator.ReplacementColumns.Count; index++)
        {
            if ((ulong)accumulator.ReplacementColumns[index] > columns)
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

    private void ResetLineState()
    {
        _currentLine = null;
        _accumulator?.Clear();
        _currentLineWrittenUntil = 0;
        _streamingPlainLine = false;
        _selectionOnly = false;
    }

    private ReplacementLineAccumulator GetAccumulator()
    {
        return _accumulator ??= new ReplacementLineAccumulator();
    }
}
