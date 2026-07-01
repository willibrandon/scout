
namespace Scout;

internal struct ReplacementMatchSink : IMatchSink
{
    private static readonly byte[] NullByte = [0];

    private readonly RawByteWriter output;
    private readonly OutputPath? prefix;
    private readonly ReadOnlyMemory<byte> fieldSeparator;
    private readonly ReadOnlyMemory<byte> replacement;
    private readonly IReadOnlyList<byte[]> patterns;
    private readonly ReplacementCapturePlan? capturePlan;
    private readonly ReplacementTemplate template;
    private readonly int[] captureStarts;
    private readonly int[] captureLengths;
    private readonly Dictionary<string, int>? captureNames;
    private readonly bool asciiCaseInsensitive;
    private readonly bool lineNumber;
    private readonly bool column;
    private readonly bool byteOffset;
    private readonly bool nullPathTerminator;
    private readonly long lineNumberOffset;
    private readonly long byteOffsetOffset;
    private readonly OutputColor color;
    private readonly ReadOnlyMemory<byte> lineTerminator;
    private long currentLineNumber;
    private long cumulativeDelta;

    public ReplacementMatchSink(
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
        ReplacementCapturePlan? capturePlan = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
        this.prefix = prefix;
        this.fieldSeparator = fieldSeparator;
        this.replacement = replacement;
        this.patterns = patterns;
        this.capturePlan = capturePlan;
        template = ReplacementTemplate.Create(replacement.Span, patterns);
        captureStarts = new int[Math.Max(1, template.HighestCapture + 1)];
        captureLengths = new int[Math.Max(1, template.HighestCapture + 1)];
        captureNames = template.UsesNamedCaptureReferences
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : null;
        this.asciiCaseInsensitive = asciiCaseInsensitive;
        this.lineNumber = lineNumber;
        this.column = column;
        this.byteOffset = byteOffset;
        this.nullPathTerminator = nullPathTerminator;
        this.lineNumberOffset = lineNumberOffset;
        this.byteOffsetOffset = byteOffsetOffset;
        this.color = color;
        this.lineTerminator = lineTerminator.IsEmpty ? "\n"u8.ToArray() : lineTerminator;
        currentLineNumber = 0;
        cumulativeDelta = 0;
    }

    public void Matched(long lineNumber, long byteOffset, long matchColumn, ReadOnlySpan<byte> match)
    {
        if (currentLineNumber != lineNumber)
        {
            currentLineNumber = lineNumber;
            cumulativeDelta = 0;
        }

        byte[] body = ReplacementFormatter.Expand(
            replacement.Span,
            match,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStarts,
            captureLengths,
            captureNames);
        long adjustedColumn = matchColumn + cumulativeDelta;
        long lineStart = byteOffset - (matchColumn - 1);
        long adjustedByteOffset = byteOffsetOffset + lineStart + adjustedColumn - 1;
        bool linked = false;
        bool hasLineNumber = this.lineNumber;
        bool hasColumn = column;
        bool hasByteOffset = this.byteOffset;

        if (prefix is not null)
        {
            linked = prefix.BeginHyperlink(output, lineNumber + lineNumberOffset, adjustedColumn);
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
            color.WriteNumberField(output, adjustedColumn);
            if (!hasByteOffset)
            {
                OutputPath.EndHyperlink(output, ref linked);
            }

            output.Write(fieldSeparator.Span);
        }

        if (hasByteOffset)
        {
            color.WriteNumberField(output, adjustedByteOffset);
            OutputPath.EndHyperlink(output, ref linked);
            output.Write(fieldSeparator.Span);
        }

        color.WriteMatch(output, body);
        output.Write(lineTerminator.Span);
        cumulativeDelta += body.Length - match.Length;
    }
}
