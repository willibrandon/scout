namespace Scout;

/// <summary>
/// Extracts replacement captures with the authoritative matcher used by regex search.
/// </summary>
/// <param name="searchPlan">The combined regex search plan.</param>
internal sealed class ReplacementCapturePlan(RegexSearchPlan searchPlan)
{
    private static readonly object s_cacheLock = new();
    private static readonly List<(
        IReadOnlyList<byte[]> Patterns,
        RegexSearchPlanOptions Options,
        ThreadLocal<ReplacementCapturePlan?> Plan)> s_cache = [];

    private readonly RegexSearchPlan _searchPlan = searchPlan;

    /// <summary>
    /// Gets the number of globally numbered capture groups.
    /// </summary>
    internal int CaptureCount => _searchPlan.CaptureCount;

    /// <summary>
    /// Gets the number of flattened start and exclusive-end capture slots required for replay.
    /// </summary>
    internal int CaptureSlotCount => _searchPlan.CaptureSlotCount;

    /// <summary>
    /// Gets the immutable global capture indexes keyed by capture name.
    /// </summary>
    internal IReadOnlyDictionary<string, int> CaptureNames => _searchPlan.CaptureNames;

    /// <summary>
    /// Creates a replacement capture plan with ordinary line-search semantics.
    /// </summary>
    /// <param name="patterns">The ordered regex patterns.</param>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <returns>The capture plan, or <see langword="null" /> for an empty pattern set.</returns>
    internal static ReplacementCapturePlan? TryCreate(
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive)
    {
        return TryCreate(
            patterns,
            new RegexSearchPlanOptions(asciiCaseInsensitive),
            searchPlan: null);
    }

    /// <summary>
    /// Creates a replacement capture plan, reusing a compatible search plan when supplied.
    /// </summary>
    /// <param name="patterns">The ordered regex patterns.</param>
    /// <param name="options">The regex search semantics.</param>
    /// <param name="searchPlan">An optional plan already used by the search.</param>
    /// <returns>The capture plan, or <see langword="null" /> for an empty pattern set.</returns>
    internal static ReplacementCapturePlan? TryCreate(
        IReadOnlyList<byte[]> patterns,
        RegexSearchPlanOptions options,
        RegexSearchPlan? searchPlan)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (searchPlan is not null && searchPlan.Options.IsCompatible(
            options.AsciiCaseInsensitive,
            options.LineRegexp,
            options.WordRegexp,
            options.Crlf,
            options.NullData,
            options.Multiline,
            options.MultilineDotall))
        {
            return new ReplacementCapturePlan(searchPlan);
        }

        ThreadLocal<ReplacementCapturePlan?>? cachedPlan = null;
        lock (s_cacheLock)
        {
            for (int index = 0; index < s_cache.Count; index++)
            {
                (IReadOnlyList<byte[]> cachedPatterns, RegexSearchPlanOptions cachedOptions, ThreadLocal<ReplacementCapturePlan?> plan) = s_cache[index];
                if (ReferenceEquals(cachedPatterns, patterns) &&
                    cachedOptions.IsCompatible(
                        options.AsciiCaseInsensitive,
                        options.LineRegexp,
                        options.WordRegexp,
                        options.Crlf,
                        options.NullData,
                        options.Multiline,
                        options.MultilineDotall))
                {
                    cachedPlan = plan;
                    break;
                }
            }

            if (cachedPlan is null)
            {
                cachedPlan = new ThreadLocal<ReplacementCapturePlan?>(() => Compile(patterns, options));
                s_cache.Add((patterns, options, cachedPlan));
            }
        }

        return cachedPlan.Value;
    }

    /// <summary>
    /// Collects globally numbered captures for a complete match span.
    /// </summary>
    /// <param name="matched">The match span reported by the authoritative search plan.</param>
    /// <param name="captureStarts">Receives capture starts relative to <paramref name="matched" />.</param>
    /// <param name="captureLengths">Receives capture lengths.</param>
    /// <param name="captureNames">Receives global capture-name mappings when requested.</param>
    /// <returns><see langword="true" /> when the same combined matcher associates the complete span with captures.</returns>
    internal bool TryCollectCaptures(
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int>? captureNames)
    {
        return TryCollectCaptures(
            matched,
            matchStart: 0,
            matched.Length,
            captureStarts,
            captureLengths,
            captureNames);
    }

    /// <summary>
    /// Collects globally numbered captures for a known span in its original haystack context.
    /// </summary>
    /// <param name="haystack">The complete record or haystack used by the authoritative match.</param>
    /// <param name="matchStart">The known match start in <paramref name="haystack" />.</param>
    /// <param name="matchLength">The known match length.</param>
    /// <param name="captureStarts">Receives capture starts relative to the known match.</param>
    /// <param name="captureLengths">Receives capture lengths.</param>
    /// <param name="captureNames">Receives global capture-name mappings when requested.</param>
    /// <returns><see langword="true" /> when the same combined matcher associates the exact span with captures.</returns>
    internal bool TryCollectCaptures(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        int[] captureStarts,
        int[] captureLengths,
        Dictionary<string, int>? captureNames)
    {
        ArgumentNullException.ThrowIfNull(captureStarts);
        ArgumentNullException.ThrowIfNull(captureLengths);
        if ((uint)matchStart > (uint)haystack.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStart));
        }

        if (matchLength < 0 || matchLength > haystack.Length - matchStart)
        {
            throw new ArgumentOutOfRangeException(nameof(matchLength));
        }

        Array.Fill(captureStarts, -1);
        Array.Fill(captureLengths, -1);
        captureNames?.Clear();
        if (captureStarts.Length > 0 && captureLengths.Length > 0)
        {
            captureStarts[0] = 0;
            captureLengths[0] = matchLength;
        }

        int[] captureSlots = new int[CaptureSlotCount];
        if (!TryCollectCaptureSlots(
            haystack,
            matchStart,
            matchLength,
            captureSlots))
        {
            return false;
        }

        int groupCount = Math.Min(
            captureSlots.Length / 2,
            Math.Min(captureStarts.Length, captureLengths.Length));
        for (int group = 0; group < groupCount; group++)
        {
            int captureStart = captureSlots[2 * group];
            int captureEnd = captureSlots[(2 * group) + 1];
            if (captureStart >= 0 && captureEnd >= captureStart)
            {
                captureStarts[group] = captureStart - matchStart;
                captureLengths[group] = captureEnd - captureStart;
            }
        }

        if (captureNames is not null)
        {
            foreach (KeyValuePair<string, int> captureName in CaptureNames)
            {
                captureNames.Add(captureName.Key, captureName.Value);
            }
        }

        return true;
    }

    /// <summary>
    /// Collects flattened capture slots for a complete match span.
    /// </summary>
    /// <param name="matched">The match span reported by the authoritative search plan.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when the same combined matcher associates the complete span with captures.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> matched,
        Span<int> captureSlots)
    {
        return TryCollectCaptureSlots(
            matched,
            matchStart: 0,
            matched.Length,
            captureSlots);
    }

    /// <summary>
    /// Collects flattened capture slots for a known span in its original haystack context.
    /// </summary>
    /// <param name="haystack">The complete record or haystack used by the authoritative match.</param>
    /// <param name="matchStart">The known match start in <paramref name="haystack" />.</param>
    /// <param name="matchLength">The known match length.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end offsets for every capture.</param>
    /// <returns><see langword="true" /> when the same combined matcher associates the exact span with captures.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> haystack,
        int matchStart,
        int matchLength,
        Span<int> captureSlots)
    {
        if ((uint)matchStart > (uint)haystack.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStart));
        }

        if (matchLength < 0 || matchLength > haystack.Length - matchStart)
        {
            throw new ArgumentOutOfRangeException(nameof(matchLength));
        }

        if (captureSlots.Length < CaptureSlotCount)
        {
            throw new ArgumentException("The capture slot buffer is too small.", nameof(captureSlots));
        }

        int matchEnd = matchStart + matchLength;
        ReadOnlySpan<byte> replayHaystack = GetReplayHaystack(haystack);
        if (matchEnd > replayHaystack.Length)
        {
            captureSlots.Fill(-1);
            return false;
        }

        return _searchPlan.TryReplayCaptures(
            replayHaystack,
            matchStart,
            matchEnd,
            captureSlots);
    }

    private ReadOnlySpan<byte> GetReplayHaystack(ReadOnlySpan<byte> haystack)
    {
        if (_searchPlan.Options.Multiline ||
            (!_searchPlan.Options.NullData &&
            !_searchPlan.Options.LineRegexp &&
            !_searchPlan.HasHaystackAnchors))
        {
            return haystack;
        }

        byte terminator = _searchPlan.Options.NullData ? (byte)0 : (byte)'\n';
        if (haystack.IsEmpty || haystack[^1] != terminator)
        {
            return haystack;
        }

        haystack = haystack[..^1];
        if (!_searchPlan.Options.NullData &&
            _searchPlan.Options.Crlf &&
            !haystack.IsEmpty &&
            haystack[^1] == (byte)'\r')
        {
            haystack = haystack[..^1];
        }

        return haystack;
    }

    private static ReplacementCapturePlan? Compile(
        IReadOnlyList<byte[]> patterns,
        RegexSearchPlanOptions options)
    {
        var searchPlan = RegexSearchPlan.Create(patterns, options);
        return searchPlan is null ? null : new ReplacementCapturePlan(searchPlan);
    }
}
