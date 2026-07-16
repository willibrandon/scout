namespace Scout;

/// <summary>
/// Writes each match after expanding a replacement template against the match's containing line.
/// </summary>
/// <param name="output">The output writer.</param>
/// <param name="prefix">The optional path prefix.</param>
/// <param name="fieldSeparator">The separator written between output fields.</param>
/// <param name="replacement">The replacement template.</param>
/// <param name="patterns">The ordered regex patterns.</param>
/// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
/// <param name="lineNumber">Whether to write line numbers.</param>
/// <param name="column">Whether to write columns.</param>
/// <param name="byteOffset">Whether to write byte offsets.</param>
/// <param name="nullPathTerminator">Whether to terminate path fields with a NUL byte.</param>
/// <param name="lineNumberOffset">The line-number offset applied to emitted matches.</param>
/// <param name="byteOffsetOffset">The byte-offset adjustment applied to emitted matches.</param>
/// <param name="color">The configured output colors.</param>
/// <param name="lineTerminator">The output line terminator.</param>
/// <param name="capturePlan">The optional authoritative capture plan.</param>
internal struct ReplacementMatchSink(
    RawByteWriter output,
    OutputPath? prefix,
    ReadOnlyMemory<byte> fieldSeparator,
    ReadOnlyMemory<byte> replacement,
    IReadOnlyList<byte[]> patterns,
    bool asciiCaseInsensitive,
    bool lineNumber,
    bool column,
    bool byteOffset,
    bool nullPathTerminator,
    long lineNumberOffset = 0,
    long byteOffsetOffset = 0,
    OutputColor color = default,
    ReadOnlyMemory<byte> lineTerminator = default,
    ReplacementCapturePlan? capturePlan = null) : IMatchLineSink
{
    private static readonly byte[] s_nullByte = [0];

    private readonly RawByteWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly OutputPath? _prefix = prefix;
    private readonly ReadOnlyMemory<byte> _fieldSeparator = fieldSeparator;
    private readonly ReadOnlyMemory<byte> _replacement = replacement;
    private readonly IReadOnlyList<byte[]> _patterns = patterns;
    private readonly ReplacementCapturePlan? _capturePlan = capturePlan;
    private readonly (
        ReplacementTemplate Template,
        int[] CaptureSlots) _captureState =
        CreateCaptureState(replacement, capturePlan);
    private readonly bool _asciiCaseInsensitive = asciiCaseInsensitive;
    private readonly bool _lineNumber = lineNumber;
    private readonly bool _column = column;
    private readonly bool _byteOffset = byteOffset;
    private readonly bool _nullPathTerminator = nullPathTerminator;
    private readonly long _lineNumberOffset = lineNumberOffset;
    private readonly long _byteOffsetOffset = byteOffsetOffset;
    private readonly OutputColor _color = color;
    private readonly ReadOnlyMemory<byte> _lineTerminator =
        lineTerminator.IsEmpty ? "\n"u8.ToArray() : lineTerminator;
    private long _currentLineNumber;
    private long _cumulativeDelta;

    /// <summary>
    /// Expands and writes one match using its original containing-line context.
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
        _ = matchByteOffset;
        if (_currentLineNumber != lineNumber)
        {
            _currentLineNumber = lineNumber;
            _cumulativeDelta = 0;
        }

        byte[] body = ReplacementFormatter.Expand(
            _replacement.Span,
            line,
            checked((int)matchColumn - 1),
            match.Length,
            _patterns,
            _asciiCaseInsensitive,
            _capturePlan,
            _captureState.Template,
            _captureState.CaptureSlots);
        long adjustedColumn = matchColumn + _cumulativeDelta;
        long adjustedByteOffset = _byteOffsetOffset + lineByteOffset + adjustedColumn - 1;
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasColumn = _column;
        bool hasByteOffset = _byteOffset;

        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(_output, lineNumber + _lineNumberOffset, adjustedColumn);
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
            _color.WriteNumberField(_output, adjustedColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            _color.WriteNumberField(_output, adjustedByteOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }

        _color.WriteMatch(_output, body);
        _output.Write(_lineTerminator.Span);
        _cumulativeDelta += body.Length - match.Length;
    }

    /// <summary>
    /// Writes an unchanged selected line when the line completed without a reportable replacement span.
    /// </summary>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineByteOffset">The zero-based line byte offset.</param>
    /// <param name="line">The completed line.</param>
    public void FinishLine(long lineNumber, long lineByteOffset, ReadOnlySpan<byte> line)
    {
        if (_currentLineNumber == lineNumber)
        {
            return;
        }

        _currentLineNumber = lineNumber;
        _cumulativeDelta = 0;
        bool linked = false;
        bool hasLineNumber = _lineNumber;
        bool hasByteOffset = _byteOffset;
        if (_prefix is not null)
        {
            linked = _prefix.BeginHyperlink(
                _output,
                lineNumber + _lineNumberOffset,
                column: null);
            _color.WritePath(_output, _prefix.Display);
            if (!hasLineNumber && !hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_nullPathTerminator ? s_nullByte : _fieldSeparator.Span);
        }

        if (hasLineNumber)
        {
            _color.WriteLineNumber(_output, lineNumber + _lineNumberOffset);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(_output, ref linked);
            }

            _output.Write(_fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            _color.WriteNumberField(_output, lineByteOffset + _byteOffsetOffset);
            OutputPath.EndHyperlink(_output, ref linked);
            _output.Write(_fieldSeparator.Span);
        }

        _output.Write(line);
        if (line.IsEmpty || line[^1] != (byte)'\n')
        {
            _output.Write(_lineTerminator.Span);
        }
    }

    private static (
        ReplacementTemplate Template,
        int[] CaptureSlots) CreateCaptureState(
            ReadOnlyMemory<byte> replacement,
            ReplacementCapturePlan? capturePlan)
    {
        var template = ReplacementTemplate.Create(
            replacement.Span,
            capturePlan?.CaptureCount ?? 0);
        int captureCount = Math.Max(template.HighestCapture, capturePlan?.CaptureCount ?? 0);
        return (
            template,
            new int[checked(2 * (captureCount + 1))]);
    }
}
