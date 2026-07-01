namespace Scout;

internal sealed class ReplacementCapturePlan
{
    private static readonly object CacheLock = new();
    private static readonly List<(IReadOnlyList<byte[]> Patterns, bool AsciiCaseInsensitive, ThreadLocal<ReplacementCapturePlan?> Plan)> Cache = [];

    private readonly RegexAutomaton?[] automata;
    private readonly SimpleReplacementCapturePlan?[] simplePlans;

    private ReplacementCapturePlan(RegexAutomaton?[] automata, SimpleReplacementCapturePlan?[] simplePlans)
    {
        this.automata = automata;
        this.simplePlans = simplePlans;
    }

    public static ReplacementCapturePlan? TryCreate(IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive)
    {
        ThreadLocal<ReplacementCapturePlan?>? cachedPlan = null;
        lock (CacheLock)
        {
            for (int index = 0; index < Cache.Count; index++)
            {
                (IReadOnlyList<byte[]> cachedPatterns, bool cachedAsciiCaseInsensitive, ThreadLocal<ReplacementCapturePlan?> plan) = Cache[index];
                if (ReferenceEquals(cachedPatterns, patterns) &&
                    cachedAsciiCaseInsensitive == asciiCaseInsensitive)
                {
                    cachedPlan = plan;
                    break;
                }
            }

            if (cachedPlan is null)
            {
                cachedPlan = new ThreadLocal<ReplacementCapturePlan?>(() => TryCompile(patterns, asciiCaseInsensitive));
                Cache.Add((patterns, asciiCaseInsensitive, cachedPlan));
            }
        }

        return cachedPlan.Value;
    }

    private static ReplacementCapturePlan? TryCompile(IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive)
    {
        var automata = new RegexAutomaton?[patterns.Count];
        var simplePlans = new SimpleReplacementCapturePlan?[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            if (SimpleReplacementCapturePlan.TryCreate(patterns[index], asciiCaseInsensitive, out SimpleReplacementCapturePlan? simplePlan))
            {
                simplePlans[index] = simplePlan;
                continue;
            }

            try
            {
                automata[index] = RegexAutomaton.Compile(
                    patterns[index],
                    asciiCaseInsensitive,
                    multiLine: false,
                    dotMatchesNewline: false);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return null;
            }
        }

        return new ReplacementCapturePlan(automata, simplePlans);
    }

    public bool TryCollectNumericCaptures(
        ReadOnlySpan<byte> matched,
        int[] captureStarts,
        int[] captureLengths)
    {
        for (int index = 0; index < automata.Length; index++)
        {
            if (simplePlans[index]?.TryCollectNumericCaptures(matched, captureStarts, captureLengths) == true)
            {
                return true;
            }

            RegexAutomaton? automaton = automata[index];
            if (automaton is null)
            {
                continue;
            }

            RegexCaptures? captures = automaton.FindCaptures(matched);
            if (captures is null ||
                captures.Match.Start != 0 ||
                captures.Match.Length != matched.Length)
            {
                continue;
            }

            Array.Fill(captureStarts, -1);
            Array.Fill(captureLengths, -1);
            int groupCount = Math.Min(captures.GroupCount, captureStarts.Length);
            for (int group = 0; group < groupCount; group++)
            {
                if (captures.GetGroup(group) is RegexMatch capture)
                {
                    captureStarts[group] = capture.Start;
                    captureLengths[group] = capture.Length;
                }
            }

            return true;
        }

        return false;
    }

    public bool TryAddExpandedNumericReplacement(
        List<byte> bytes,
        ReadOnlySpan<byte> matched,
        ReplacementTemplate template)
    {
        if (template.UsesNamedCaptureReferences)
        {
            return false;
        }

        for (int index = 0; index < simplePlans.Length; index++)
        {
            if (simplePlans[index]?.TryAddExpandedNumericReplacement(bytes, matched, template) == true)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryWriteExpandedNumericReplacement(
        RawByteWriter output,
        ReadOnlySpan<byte> matched,
        ReplacementTemplate template)
    {
        if (template.UsesNamedCaptureReferences)
        {
            return false;
        }

        for (int index = 0; index < simplePlans.Length; index++)
        {
            if (simplePlans[index]?.TryWriteExpandedNumericReplacement(output, matched, template) == true)
            {
                return true;
            }
        }

        return false;
    }
}
