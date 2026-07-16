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
        int[]? captureSlotsBuffer = null)
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
                captureSlotsBuffer);
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
        int[]? captureSlotsBuffer = null)
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
            captureSlotsBuffer);
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
        int[]? captureSlotsBuffer = null)
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
            captureSlotsBuffer);
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
        int[]? captureSlotsBuffer = null)
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
                captureSlotsBuffer);
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
        int[]? captureSlotsBuffer)
    {
        WriteExpanded(
            output,
            replacement,
            matched,
            matchStart: 0,
            matched.Length,
            patterns,
            asciiCaseInsensitive,
            capturePlan,
            template,
            captureSlotsBuffer);
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
        int[]? captureSlotsBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, capturePlan?.CaptureCount ?? 0);
        ReplacementCapturePlan? activeCapturePlan = GetOrCreateCapturePlan(
            patterns,
            asciiCaseInsensitive,
            template,
            capturePlan);
        int captureCount = Math.Max(template.HighestCapture, activeCapturePlan?.CaptureCount ?? 0);
        int requiredLength = checked(2 * (captureCount + 1));
        int[] captureSlots = captureSlotsBuffer is not null && captureSlotsBuffer.Length >= requiredLength
            ? captureSlotsBuffer
            : CreateCaptureSlots(captureCount);
        if (activeCapturePlan is null ||
            !activeCapturePlan.TryCollectCaptureSlots(
                haystack,
                matchStart,
                matchLength,
                captureSlots))
        {
            InitializeWholeMatch(haystack, matchStart, matchLength, captureSlots);
        }

        template.WriteExpanded(
            output,
            haystack,
            captureSlots,
            activeCapturePlan?.CaptureNames);
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
        int[]? captureSlotsBuffer)
    {
        ReplacementCapturePlan? activeCapturePlan = GetOrCreateCapturePlan(
            patterns,
            asciiCaseInsensitive,
            template,
            capturePlan);
        int captureCount = Math.Max(template.HighestCapture, activeCapturePlan?.CaptureCount ?? 0);
        int requiredLength = checked(2 * (captureCount + 1));
        int[] captureSlots = captureSlotsBuffer is not null && captureSlotsBuffer.Length >= requiredLength
            ? captureSlotsBuffer
            : CreateCaptureSlots(captureCount);
        if (activeCapturePlan is null ||
            !activeCapturePlan.TryCollectCaptureSlots(
                haystack,
                matchStart,
                matchLength,
                captureSlots))
        {
            InitializeWholeMatch(haystack, matchStart, matchLength, captureSlots);
        }

        template.AddExpanded(
            bytes,
            haystack,
            captureSlots,
            activeCapturePlan?.CaptureNames);
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

    private static int[] CreateCaptureSlots(int captureCount)
    {
        int[] values = new int[Math.Max(2, checked(2 * (captureCount + 1)))];
        Array.Fill(values, -1);
        return values;
    }

    private static void InitializeWholeMatch(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        Span<int> captureSlots)
    {
        _ = haystack.Slice(matchStart, matchLength);
        captureSlots.Fill(-1);
        captureSlots[0] = matchStart;
        captureSlots[1] = matchStart + matchLength;
    }

    private static void Add(List<byte> bytes, ReadOnlySpan<byte> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            bytes.Add(values[index]);
        }
    }
}
