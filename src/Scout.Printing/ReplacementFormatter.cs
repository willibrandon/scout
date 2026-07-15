namespace Scout;

/// <summary>
/// Expands replacement templates with captures associated by the active regex engine.
/// </summary>
internal static class ReplacementFormatter
{
    internal static byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        List<long> replacementColumns,
        List<int>? replacementLengths = null,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, capturePlan?.CaptureCount ?? 0);
        replacementColumns.Clear();
        replacementLengths?.Clear();
        var bytes = new List<byte>(line.Length);
        int previous = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            Add(bytes, line[previous..start]);
            replacementColumns.Add(bytes.Count + 1L);
            int replacementStart = bytes.Count;
            AddExpanded(
                bytes,
                line,
                start,
                lengths[index],
                patterns,
                asciiCaseInsensitive,
                capturePlan,
                template,
                captureStartsBuffer,
                captureLengthsBuffer,
                captureNamesBuffer);
            replacementLengths?.Add(bytes.Count - replacementStart);
            previous = start + lengths[index];
        }

        Add(bytes, line[previous..]);
        return bytes.ToArray();
    }

    internal static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        return Expand(
            replacement,
            matched,
            matchStart: 0,
            matched.Length,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer);
    }

    internal static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, capturePlan?.CaptureCount ?? 0);
        var bytes = new List<byte>(template.LiteralLength + matchLength);
        AddExpanded(
            bytes,
            haystack,
            matchStart,
            matchLength,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer);
        return bytes.ToArray();
    }

    internal static void WriteReplacedLine(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan = null,
        ReplacementTemplate? template = null,
        int[]? captureStartsBuffer = null,
        int[]? captureLengthsBuffer = null,
        Dictionary<string, int>? captureNamesBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, capturePlan?.CaptureCount ?? 0);
        int previous = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            output.Write(line[previous..start]);
            WriteExpanded(
                output,
                replacement,
                line,
                start,
                lengths[index],
                patterns,
                asciiCaseInsensitive,
                capturePlan,
                template,
                captureStartsBuffer,
                captureLengthsBuffer,
                captureNamesBuffer);
            previous = start + lengths[index];
        }

        output.Write(line[previous..]);
    }

    internal static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate? template,
        int[]? captureStartsBuffer,
        int[]? captureLengthsBuffer,
        Dictionary<string, int>? captureNamesBuffer)
    {
        output.Write(Expand(
            replacement,
            matched,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer));
    }

    internal static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate? template,
        int[]? captureStartsBuffer,
        int[]? captureLengthsBuffer,
        Dictionary<string, int>? captureNamesBuffer)
    {
        output.Write(Expand(
            replacement,
            haystack,
            matchStart,
            matchLength,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureStartsBuffer,
            captureLengthsBuffer,
            captureNamesBuffer));
    }

    private static void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementCapturePlan? capturePlan,
        ReplacementTemplate template,
        int[]? captureStartsBuffer,
        int[]? captureLengthsBuffer,
        Dictionary<string, int>? captureNamesBuffer)
    {
        ReadOnlySpan<byte> matched = haystack.Slice(matchStart, matchLength);
        ReplacementCapturePlan? activeCapturePlan = GetOrCreateCapturePlan(
            patterns,
            asciiCaseInsensitive,
            template,
            capturePlan);
        int captureCount = Math.Max(template.HighestCapture, activeCapturePlan?.CaptureCount ?? 0);
        int requiredLength = checked(captureCount + 1);
        int[] captureStarts = captureStartsBuffer is not null && captureStartsBuffer.Length >= requiredLength
            ? captureStartsBuffer
            : CreateCaptureArray(captureCount);
        int[] captureLengths = captureLengthsBuffer is not null && captureLengthsBuffer.Length >= requiredLength
            ? captureLengthsBuffer
            : CreateCaptureArray(captureCount);
        Dictionary<string, int>? captureNames = template.UsesNamedCaptureReferences
            ? captureNamesBuffer ?? new Dictionary<string, int>(StringComparer.Ordinal)
            : null;

        if (activeCapturePlan is not null)
        {
            _ = activeCapturePlan.TryCollectCaptures(
                haystack,
                matchStart,
                matchLength,
                captureStarts,
                captureLengths,
                captureNames);
        }
        else
        {
            InitializeWholeMatch(matched, captureStarts, captureLengths);
            captureNames?.Clear();
        }

        template.AddExpanded(bytes, matched, captureStarts, captureLengths, captureNames);
    }

    private static ReplacementCapturePlan? GetOrCreateCapturePlan(
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ReplacementTemplate template,
        ReplacementCapturePlan? capturePlan)
    {
        if (capturePlan is not null || !template.RequiresSubcaptures)
        {
            return capturePlan;
        }

        try
        {
            return ReplacementCapturePlan.TryCreate(patterns, asciiCaseInsensitive);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static int[] CreateCaptureArray(int captureCount)
    {
        int[] values = new int[Math.Max(1, checked(captureCount + 1))];
        Array.Fill(values, -1);
        return values;
    }

    private static void InitializeWholeMatch(
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths)
    {
        Array.Fill(captureStarts, -1);
        Array.Fill(captureLengths, -1);
        if (captureStarts.Length > 0 && captureLengths.Length > 0)
        {
            captureStarts[0] = 0;
            captureLengths[0] = matched.Length;
        }
    }

    private static void Add(List<byte> bytes, ReadOnlySpan<byte> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            bytes.Add(values[index]);
        }
    }
}
