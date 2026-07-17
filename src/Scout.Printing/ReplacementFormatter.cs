namespace Scout;

/// <summary>
/// Expands replacement templates with captures associated by the active regex engine.
/// </summary>
internal static class ReplacementFormatter
{
    /// <summary>
    /// Replaces known native-regex matches in one line and records replacement output positions.
    /// </summary>
    /// <param name="line">The complete line.</param>
    /// <param name="starts">The match starts in <paramref name="line" />.</param>
    /// <param name="lengths">The match lengths.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="replacementColumns">Receives one-based replacement columns.</param>
    /// <param name="replacementLengths">Receives replacement lengths when supplied.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The replaced line.</returns>
    internal static byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        List<long> replacementColumns,
        List<int>? replacementLengths = null,
        RegexSearchPlan? searchPlan = null,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        return ReplaceLineCore(
            line,
            line,
            lineStartInHaystack: 0,
            starts,
            lengths,
            replacement,
            replacementColumns,
            replacementLengths,
            searchPlan,
            searchStarts: null,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Replaces known matches in one line using their authoritative capture provider.
    /// </summary>
    /// <param name="line">The complete line and capture haystack.</param>
    /// <param name="starts">The match starts in <paramref name="line" />.</param>
    /// <param name="lengths">The match lengths.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="replacementColumns">Receives one-based replacement columns.</param>
    /// <param name="replacementLengths">Receives replacement lengths when supplied.</param>
    /// <param name="captureProvider">The authoritative capture provider.</param>
    /// <param name="searchStarts">The successful engine-search start for each match.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The replaced line.</returns>
    internal static byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        List<long> replacementColumns,
        List<int>? replacementLengths,
        IReplacementCaptureProvider captureProvider,
        IReadOnlyList<int> searchStarts,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(captureProvider);
        ArgumentNullException.ThrowIfNull(searchStarts);
        if (searchStarts.Count != starts.Count)
        {
            throw new ArgumentException("The search-start count must match the replacement count.", nameof(searchStarts));
        }

        return ReplaceLineCore(
            line,
            line,
            lineStartInHaystack: 0,
            starts,
            lengths,
            replacement,
            replacementColumns,
            replacementLengths,
            captureProvider,
            searchStarts,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Replaces matches in a line slice while replaying captures against the complete haystack.
    /// </summary>
    /// <param name="line">The line slice to replace.</param>
    /// <param name="captureHaystack">The complete capture haystack.</param>
    /// <param name="lineStartInHaystack">The line start in <paramref name="captureHaystack" />.</param>
    /// <param name="starts">The match starts in <paramref name="line" />.</param>
    /// <param name="lengths">The match lengths.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="replacementColumns">Receives one-based replacement columns.</param>
    /// <param name="replacementLengths">Receives replacement lengths when supplied.</param>
    /// <param name="captureProvider">The authoritative capture provider.</param>
    /// <param name="searchStarts">The successful engine-search start for each match.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The replaced line.</returns>
    internal static byte[] ReplaceLine(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> captureHaystack,
        int lineStartInHaystack,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        List<long> replacementColumns,
        List<int>? replacementLengths,
        IReplacementCaptureProvider captureProvider,
        IReadOnlyList<int> searchStarts,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(captureProvider);
        ArgumentNullException.ThrowIfNull(searchStarts);
        if ((uint)lineStartInHaystack > (uint)captureHaystack.Length ||
            line.Length > captureHaystack.Length - lineStartInHaystack)
        {
            throw new ArgumentOutOfRangeException(nameof(lineStartInHaystack));
        }

        if (searchStarts.Count != starts.Count)
        {
            throw new ArgumentException("The search-start count must match the replacement count.", nameof(searchStarts));
        }

        return ReplaceLineCore(
            line,
            captureHaystack,
            lineStartInHaystack,
            starts,
            lengths,
            replacement,
            replacementColumns,
            replacementLengths,
            captureProvider,
            searchStarts,
            template,
            captureSlotsBuffer);
    }

    private static byte[] ReplaceLineCore(
        ReadOnlySpan<byte> line,
        ReadOnlySpan<byte> captureHaystack,
        int lineStartInHaystack,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        List<long> replacementColumns,
        List<int>? replacementLengths,
        IReplacementCaptureProvider? captureProvider,
        IReadOnlyList<int>? searchStarts,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, captureProvider?.CaptureCount ?? 0);
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
                captureHaystack,
                lineStartInHaystack + start,
                lengths[index],
                captureProvider,
                searchStarts?[index] ?? start,
                template,
                captureSlotsBuffer);
            replacementLengths?.Add(bytes.Count - replacementStart);
            previous = start + lengths[index];
        }

        Add(bytes, line[previous..]);
        return bytes.ToArray();
    }

    /// <summary>
    /// Expands a replacement for a complete native-regex match span.
    /// </summary>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="matched">The complete matched span.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The expanded replacement.</returns>
    internal static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        RegexSearchPlan? searchPlan = null,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        return Expand(
            replacement,
            matched,
            matchStart: 0,
            matched.Length,
            searchPlan,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Expands a replacement for a known match using its authoritative capture provider.
    /// </summary>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="haystack">The complete capture haystack.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="captureProvider">The authoritative capture provider.</param>
    /// <param name="searchStart">The start of the successful engine search.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The expanded replacement.</returns>
    internal static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReplacementCaptureProvider captureProvider,
        int searchStart,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(captureProvider);
        template ??= ReplacementTemplate.Create(replacement, captureProvider.CaptureCount);
        var bytes = new List<byte>(template.LiteralLength + matchLength);
        AddExpanded(
            bytes,
            haystack,
            matchStart,
            matchLength,
            captureProvider,
            searchStart,
            template,
            captureSlotsBuffer);
        return bytes.ToArray();
    }

    /// <summary>
    /// Expands a replacement for a known native-regex match in its original haystack.
    /// </summary>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="haystack">The complete capture haystack.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    /// <returns>The expanded replacement.</returns>
    internal static byte[] Expand(
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        RegexSearchPlan? searchPlan = null,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        template ??= ReplacementTemplate.Create(replacement, searchPlan?.CaptureCount ?? 0);
        var bytes = new List<byte>(template.LiteralLength + matchLength);
        AddExpanded(
            bytes,
            haystack,
            matchStart,
            matchLength,
            searchPlan,
            matchStart,
            template,
            captureSlotsBuffer);
        return bytes.ToArray();
    }

    /// <summary>
    /// Writes one line with known native-regex matches replaced.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="line">The complete line.</param>
    /// <param name="starts">The match starts.</param>
    /// <param name="lengths">The match lengths.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    internal static void WriteReplacedLine(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        RegexSearchPlan? searchPlan = null,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        WriteReplacedLineCore(
            output,
            line,
            starts,
            lengths,
            replacement,
            searchPlan,
            searchStarts: null,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Writes one line with known matches replaced through their authoritative capture provider.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="line">The complete line.</param>
    /// <param name="starts">The match starts.</param>
    /// <param name="lengths">The match lengths.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="captureProvider">The authoritative capture provider.</param>
    /// <param name="searchStarts">The successful engine-search start for each match.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    internal static void WriteReplacedLine(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReplacementCaptureProvider captureProvider,
        IReadOnlyList<int> searchStarts,
        ReplacementTemplate? template = null,
        int[]? captureSlotsBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(captureProvider);
        ArgumentNullException.ThrowIfNull(searchStarts);
        if (searchStarts.Count != starts.Count)
        {
            throw new ArgumentException("The search-start count must match the replacement count.", nameof(searchStarts));
        }

        WriteReplacedLineCore(
            output,
            line,
            starts,
            lengths,
            replacement,
            captureProvider,
            searchStarts,
            template,
            captureSlotsBuffer);
    }

    private static void WriteReplacedLineCore(
        RawByteWriter output,
        ReadOnlySpan<byte> line,
        IReadOnlyList<int> starts,
        IReadOnlyList<int> lengths,
        ReadOnlySpan<byte> replacement,
        IReplacementCaptureProvider? captureProvider,
        IReadOnlyList<int>? searchStarts,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, captureProvider?.CaptureCount ?? 0);
        int previous = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            int start = starts[index];
            output.Write(line[previous..start]);
            WriteExpandedCore(
                output,
                replacement,
                line,
                start,
                lengths[index],
                captureProvider,
                searchStarts?[index] ?? start,
                template,
                captureSlotsBuffer);
            previous = start + lengths[index];
        }

        output.Write(line[previous..]);
    }

    /// <summary>
    /// Writes an expanded replacement for a complete native-regex match span.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="matched">The complete matched span.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    internal static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> matched,
        RegexSearchPlan? searchPlan,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        WriteExpandedCore(
            output,
            replacement,
            matched,
            matchStart: 0,
            matched.Length,
            searchPlan,
            searchStart: 0,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Writes an expanded replacement for a native-regex match in its original haystack.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="haystack">The complete capture haystack.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="searchPlan">The authoritative native regex plan.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    internal static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        RegexSearchPlan? searchPlan,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        WriteExpandedCore(
            output,
            replacement,
            haystack,
            matchStart,
            matchLength,
            searchPlan,
            matchStart,
            template,
            captureSlotsBuffer);
    }

    /// <summary>
    /// Writes an expanded replacement through the authoritative capture provider.
    /// </summary>
    /// <param name="output">The output writer.</param>
    /// <param name="replacement">The replacement template.</param>
    /// <param name="haystack">The complete capture haystack.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="captureProvider">The authoritative capture provider.</param>
    /// <param name="searchStart">The start of the successful engine search.</param>
    /// <param name="template">A reusable parsed replacement template.</param>
    /// <param name="captureSlotsBuffer">A reusable capture-slot buffer.</param>
    internal static void WriteExpanded(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReplacementCaptureProvider captureProvider,
        int searchStart,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        ArgumentNullException.ThrowIfNull(captureProvider);
        WriteExpandedCore(
            output,
            replacement,
            haystack,
            matchStart,
            matchLength,
            captureProvider,
            searchStart,
            template,
            captureSlotsBuffer);
    }

    private static void WriteExpandedCore(
        RawByteWriter output,
        ReadOnlySpan<byte> replacement,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReplacementCaptureProvider? captureProvider,
        int searchStart,
        ReplacementTemplate? template,
        int[]? captureSlotsBuffer)
    {
        template ??= ReplacementTemplate.Create(replacement, captureProvider?.CaptureCount ?? 0);
        int captureCount = Math.Max(template.HighestCapture, captureProvider?.CaptureCount ?? 0);
        int requiredLength = checked(2 * (captureCount + 1));
        int[] captureSlots = captureSlotsBuffer is not null && captureSlotsBuffer.Length >= requiredLength
            ? captureSlotsBuffer
            : CreateCaptureSlots(captureCount);
        if (captureProvider is null ||
            !captureProvider.TryCollectCaptureSlots(
                haystack,
                matchStart,
                matchLength,
                searchStart,
                captureSlots))
        {
            InitializeWholeMatch(haystack, matchStart, matchLength, captureSlots);
        }

        template.WriteExpanded(
            output,
            haystack,
            captureSlots,
            captureProvider?.CaptureNames);
    }

    private static void AddExpanded(
        List<byte> bytes,
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        IReplacementCaptureProvider? captureProvider,
        int searchStart,
        ReplacementTemplate template,
        int[]? captureSlotsBuffer)
    {
        int captureCount = Math.Max(template.HighestCapture, captureProvider?.CaptureCount ?? 0);
        int requiredLength = checked(2 * (captureCount + 1));
        int[] captureSlots = captureSlotsBuffer is not null && captureSlotsBuffer.Length >= requiredLength
            ? captureSlotsBuffer
            : CreateCaptureSlots(captureCount);
        if (captureProvider is null ||
            !captureProvider.TryCollectCaptureSlots(
                haystack,
                matchStart,
                matchLength,
                searchStart,
                captureSlots))
        {
            InitializeWholeMatch(haystack, matchStart, matchLength, captureSlots);
        }

        template.AddExpanded(
            bytes,
            haystack,
            captureSlots,
            captureProvider?.CaptureNames);
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
