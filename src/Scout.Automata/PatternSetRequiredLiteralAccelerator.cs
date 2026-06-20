namespace Scout;

internal sealed class PatternSetRequiredLiteralAccelerator
{
    private const int MaxEagerDenseRequiredLiteralStates = 2048;

    private readonly AhoCorasickAutomaton automaton;
    private readonly PatternSetRequiredAutomaton[][] automataByLiteral;
    private readonly int[] maxLookBehindByLiteral;
    private readonly int maxLookBehind;
    private readonly bool[] acceleratedAutomata;
    private readonly bool[]?[]? startBytesByAutomaton;
    private readonly PatternSetRequiredLiteralGuard?[]? guardsByAutomaton;

    public PatternSetRequiredLiteralAccelerator(
        IReadOnlyList<PatternSetRequiredLiteralEntry> entries,
        int automataCount,
        IReadOnlyList<RegexAutomaton>? automata = null,
        IReadOnlyList<PatternSetRequiredLiteralGuard?>? guards = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(automataCount);

        acceleratedAutomata = new bool[automataCount];
        startBytesByAutomaton = BuildStartBytes(automata, automataCount);
        guardsByAutomaton = BuildGuards(guards, automataCount);
        var literals = new List<byte[]>();
        var automataByLiteral = new List<List<PatternSetRequiredAutomaton>>();
        var maxLookBehindByLiteral = new List<int>();
        int maxLookBehind = 0;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            PatternSetRequiredLiteralEntry entry = entries[entryIndex];
            if ((uint)entry.AutomatonIndex >= (uint)automataCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "automaton index is outside the set");
            }

            acceleratedAutomata[entry.AutomatonIndex] = true;
            for (int literalIndex = 0; literalIndex < entry.Literals.Length; literalIndex++)
            {
                PatternSetRequiredLiteral requiredLiteral = entry.Literals[literalIndex];
                byte[] literal = NormalizeAsciiCase(requiredLiteral.Literal);
                int lookBehind = Math.Min(requiredLiteral.MaxLookBehind, RegexPrefilter.RequiredLiteralLookBehind);
                int existing = FindLiteral(literals, literal);
                if (existing < 0)
                {
                    existing = literals.Count;
                    literals.Add(literal);
                    automataByLiteral.Add([]);
                    maxLookBehindByLiteral.Add(0);
                }

                AddDistinctAutomaton(automataByLiteral[existing], entry.AutomatonIndex, lookBehind);
                maxLookBehindByLiteral[existing] = Math.Max(maxLookBehindByLiteral[existing], lookBehind);
                maxLookBehind = Math.Max(maxLookBehind, lookBehind);
            }
        }

        byte[][] literalArray = literals.ToArray();
        automaton = AhoCorasickAutomaton.Create(
            literalArray,
            AhoCorasickMatchKind.Standard,
            asciiCaseInsensitive: true);
        automaton.EnsureDenseTransitions(MaxEagerDenseRequiredLiteralStates);
        this.automataByLiteral = new PatternSetRequiredAutomaton[automataByLiteral.Count][];
        for (int index = 0; index < automataByLiteral.Count; index++)
        {
            this.automataByLiteral[index] = automataByLiteral[index].ToArray();
        }

        this.maxLookBehindByLiteral = maxLookBehindByLiteral.ToArray();
        this.maxLookBehind = maxLookBehind;
    }

    public bool UsesGuards
    {
        get
        {
            if (guardsByAutomaton is null)
            {
                return false;
            }

            for (int index = 0; index < guardsByAutomaton.Length; index++)
            {
                if (guardsByAutomaton[index] is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool CoversAutomaton(int automatonIndex)
    {
        return acceleratedAutomata[automatonIndex];
    }

    public bool CoversAllAutomata
    {
        get
        {
            for (int index = 0; index < acceleratedAutomata.Length; index++)
            {
                if (!acceleratedAutomata[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public long CountMatches(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        RegexAutomaton[] automata,
        int[] automataPatternIds)
    {
        long count = 0;
        int offset = Math.Clamp(startOffset, 0, haystack.Length);
        int searchOffset = offset;
        ReadOnlySpan<byte> search = haystack[searchOffset..];
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(search);
        var deferredHits = new List<AhoCorasickMatch>();
        Span<int> nextStartToTry = automata.Length <= 256
            ? stackalloc int[automata.Length]
            : new int[automata.Length];
        while (offset <= haystack.Length)
        {
            DropDeferredHitsBefore(deferredHits, searchOffset, offset);
            PatternSetMatch? best = null;
            nextStartToTry.Fill(offset);
            int deferredIndex = 0;
            while (true)
            {
                AhoCorasickMatch literal;
                bool fromDeferred;
                if (deferredIndex < deferredHits.Count)
                {
                    literal = deferredHits[deferredIndex++];
                    fromDeferred = true;
                }
                else if (matches.MoveNext())
                {
                    literal = matches.Current;
                    fromDeferred = false;
                }
                else
                {
                    break;
                }

                int requiredAt = searchOffset + literal.Start;
                if (requiredAt < offset)
                {
                    continue;
                }

                if (best.HasValue &&
                    requiredAt - maxLookBehind > best.Value.Match.Start)
                {
                    if (!fromDeferred)
                    {
                        deferredHits.Add(literal);
                    }

                    break;
                }

                if (best.HasValue && !fromDeferred)
                {
                    deferredHits.Add(literal);
                }

                if (best.HasValue &&
                    requiredAt - maxLookBehindByLiteral[literal.PatternId] > best.Value.Match.Start)
                {
                    continue;
                }

                TryImproveBest(haystack, offset, requiredAt, literal.PatternId, automata, automataPatternIds, nextStartToTry, ref best);
            }

            if (!best.HasValue)
            {
                break;
            }

            count++;
            RegexMatch match = best.Value.Match;
            offset = match.Length == 0
                ? Math.Min(match.End + 1, haystack.Length + 1)
                : match.End;
        }

        return count;
    }

    private static void DropDeferredHitsBefore(List<AhoCorasickMatch> deferredHits, int searchOffset, int offset)
    {
        int dropCount = 0;
        while (dropCount < deferredHits.Count &&
            searchOffset + deferredHits[dropCount].Start < offset)
        {
            dropCount++;
        }

        if (dropCount != 0)
        {
            deferredHits.RemoveRange(0, dropCount);
        }
    }

    public PatternSetMatch? Find(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        PatternSetMatch? best)
    {
        Span<int> nextStartToTry = automata.Length <= 256
            ? stackalloc int[automata.Length]
            : new int[automata.Length];
        nextStartToTry.Fill(startOffset);
        ReadOnlySpan<byte> search = haystack[startOffset..];
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(search);
        while (matches.MoveNext())
        {
            AhoCorasickMatch literal = matches.Current;
            int requiredAt = startOffset + literal.Start;
            if (best.HasValue &&
                requiredAt - maxLookBehind > best.Value.Match.Start)
            {
                break;
            }

            if (best.HasValue &&
                requiredAt - maxLookBehindByLiteral[literal.PatternId] > best.Value.Match.Start)
            {
                continue;
            }

            PatternSetRequiredAutomaton[] candidateAutomata = automataByLiteral[literal.PatternId];
            for (int index = 0; index < candidateAutomata.Length; index++)
            {
                PatternSetRequiredAutomaton candidateAutomaton = candidateAutomata[index];
                int automatonIndex = candidateAutomaton.AutomatonIndex;
                int firstStart = Math.Max(startOffset, requiredAt - candidateAutomaton.MaxLookBehind);
                firstStart = Math.Max(firstStart, nextStartToTry[automatonIndex]);
                int lastStart = best.HasValue
                    ? Math.Min(requiredAt, best.Value.Match.Start)
                    : requiredAt;
                bool[]? startBytes = startBytesByAutomaton?[automatonIndex];
                for (int start = firstStart; start <= lastStart; start++)
                {
                    if (startBytes is not null &&
                        (start >= haystack.Length || !startBytes[haystack[start]]))
                    {
                        continue;
                    }

                    PatternSetRequiredLiteralGuard? guard = guardsByAutomaton?[automatonIndex];
                    if (guard is not null && !guard.CanMatchAt(haystack, start))
                    {
                        continue;
                    }

                    RegexMatch? match = automata[automatonIndex].MatchAt(haystack, start);
                    if (!match.HasValue)
                    {
                        continue;
                    }

                    var candidate = new PatternSetMatch(automataPatternIds[automatonIndex], match.Value);
                    if (PatternSet.IsBetter(candidate, best))
                    {
                        best = candidate;
                        lastStart = Math.Min(lastStart, best.Value.Match.Start);
                    }
                }

                nextStartToTry[automatonIndex] = Math.Max(nextStartToTry[automatonIndex], requiredAt + 1);
            }
        }

        return best;
    }

    private void TryImproveBest(
        ReadOnlySpan<byte> haystack,
        int offset,
        int requiredAt,
        int literalPatternId,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        Span<int> nextStartToTry,
        ref PatternSetMatch? best)
    {
        PatternSetRequiredAutomaton[] candidateAutomata = automataByLiteral[literalPatternId];
        for (int index = 0; index < candidateAutomata.Length; index++)
        {
            PatternSetRequiredAutomaton candidateAutomaton = candidateAutomata[index];
            int automatonIndex = candidateAutomaton.AutomatonIndex;
            int firstStart = Math.Max(offset, requiredAt - candidateAutomaton.MaxLookBehind);
            firstStart = Math.Max(firstStart, nextStartToTry[automatonIndex]);
            int lastStart = best.HasValue
                ? Math.Min(requiredAt, best.Value.Match.Start)
                : requiredAt;
            bool[]? startBytes = startBytesByAutomaton?[automatonIndex];
            for (int start = firstStart; start <= lastStart; start++)
            {
                if (startBytes is not null &&
                    (start >= haystack.Length || !startBytes[haystack[start]]))
                {
                    continue;
                }

                PatternSetRequiredLiteralGuard? guard = guardsByAutomaton?[automatonIndex];
                if (guard is not null && !guard.CanMatchAt(haystack, start))
                {
                    continue;
                }

                RegexMatch? match = automata[automatonIndex].MatchAt(haystack, start);
                if (!match.HasValue)
                {
                    continue;
                }

                var candidate = new PatternSetMatch(automataPatternIds[automatonIndex], match.Value);
                if (PatternSet.IsBetter(candidate, best))
                {
                    best = candidate;
                    lastStart = Math.Min(lastStart, best.Value.Match.Start);
                }
            }

            nextStartToTry[automatonIndex] = Math.Max(nextStartToTry[automatonIndex], requiredAt + 1);
        }
    }

    private static int FindLiteral(List<byte[]> literals, byte[] literal)
    {
        for (int index = 0; index < literals.Count; index++)
        {
            if (literals[index].AsSpan().SequenceEqual(literal))
            {
                return index;
            }
        }

        return -1;
    }

    private static void AddDistinctAutomaton(List<PatternSetRequiredAutomaton> automata, int automatonIndex, int maxLookBehind)
    {
        for (int index = 0; index < automata.Count; index++)
        {
            if (automata[index].AutomatonIndex == automatonIndex)
            {
                automata[index] = new PatternSetRequiredAutomaton(
                    automatonIndex,
                    Math.Max(automata[index].MaxLookBehind, maxLookBehind));
                return;
            }
        }

        automata.Add(new PatternSetRequiredAutomaton(automatonIndex, maxLookBehind));
    }

    private static bool[]?[]? BuildStartBytes(IReadOnlyList<RegexAutomaton>? automata, int automataCount)
    {
        if (automata is null)
        {
            return null;
        }

        if (automata.Count != automataCount)
        {
            throw new ArgumentException("automata count does not match required-literal entries", nameof(automata));
        }

        bool[]?[] startBytes = new bool[]?[automataCount];
        for (int index = 0; index < automata.Count; index++)
        {
            bool[] bytes = new bool[256];
            if (automata[index].TryAddStartBytes(bytes))
            {
                startBytes[index] = bytes;
            }
        }

        return startBytes;
    }

    private static PatternSetRequiredLiteralGuard?[]? BuildGuards(IReadOnlyList<PatternSetRequiredLiteralGuard?>? guards, int automataCount)
    {
        if (guards is null)
        {
            return null;
        }

        if (guards.Count != automataCount)
        {
            throw new ArgumentException("guard count does not match automata count", nameof(guards));
        }

        var copied = new PatternSetRequiredLiteralGuard?[automataCount];
        bool any = false;
        for (int index = 0; index < guards.Count; index++)
        {
            PatternSetRequiredLiteralGuard? guard = guards[index];
            copied[index] = guard;
            any |= guard is not null;
        }

        return any ? copied : null;
    }

    private static byte[] NormalizeAsciiCase(byte[] literal)
    {
        byte[] normalized = literal.ToArray();
        for (int index = 0; index < normalized.Length; index++)
        {
            byte value = normalized[index];
            if (value is >= (byte)'A' and <= (byte)'Z')
            {
                normalized[index] = (byte)(value + 32);
            }
        }

        return normalized;
    }
}
