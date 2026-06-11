namespace Scout;

internal sealed class PatternSetRequiredLiteralAccelerator
{
    private readonly AhoCorasickAutomaton automaton;
    private readonly int[][] automataByLiteral;
    private readonly bool[] acceleratedAutomata;

    public PatternSetRequiredLiteralAccelerator(IReadOnlyList<PatternSetRequiredLiteralEntry> entries, int automataCount)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(automataCount);

        acceleratedAutomata = new bool[automataCount];
        var literals = new List<byte[]>();
        var automataByLiteral = new List<List<int>>();
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
                byte[] literal = NormalizeAsciiCase(entry.Literals[literalIndex]);
                int existing = FindLiteral(literals, literal);
                if (existing < 0)
                {
                    existing = literals.Count;
                    literals.Add(literal);
                    automataByLiteral.Add([]);
                }

                AddDistinctAutomaton(automataByLiteral[existing], entry.AutomatonIndex);
            }
        }

        byte[][] literalArray = literals.ToArray();
        automaton = AhoCorasickAutomaton.Create(
            literalArray,
            AhoCorasickMatchKind.Standard,
            asciiCaseInsensitive: true);
        this.automataByLiteral = new int[automataByLiteral.Count][];
        for (int index = 0; index < automataByLiteral.Count; index++)
        {
            this.automataByLiteral[index] = automataByLiteral[index].ToArray();
        }
    }

    public bool CoversAutomaton(int automatonIndex)
    {
        return acceleratedAutomata[automatonIndex];
    }

    public PatternSetMatch? Find(
        ReadOnlySpan<byte> haystack,
        int startOffset,
        RegexAutomaton[] automata,
        int[] automataPatternIds,
        PatternSetMatch? best)
    {
        int[] nextStartToTry = new int[automata.Length];
        Array.Fill(nextStartToTry, startOffset);
        ReadOnlySpan<byte> search = haystack[startOffset..];
        AhoCorasickOverlappingEnumerator matches = automaton.EnumerateOverlapping(search);
        while (matches.MoveNext())
        {
            AhoCorasickMatch literal = matches.Current;
            int requiredAt = startOffset + literal.Start;
            if (best.HasValue &&
                requiredAt - RegexPrefilter.RequiredLiteralLookBehind > best.Value.Match.Start)
            {
                break;
            }

            int[] candidateAutomata = automataByLiteral[literal.PatternId];
            for (int index = 0; index < candidateAutomata.Length; index++)
            {
                int automatonIndex = candidateAutomata[index];
                int firstStart = Math.Max(startOffset, requiredAt - RegexPrefilter.RequiredLiteralLookBehind);
                firstStart = Math.Max(firstStart, nextStartToTry[automatonIndex]);
                int lastStart = best.HasValue
                    ? Math.Min(requiredAt, best.Value.Match.Start)
                    : requiredAt;
                for (int start = firstStart; start <= lastStart; start++)
                {
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

    private static void AddDistinctAutomaton(List<int> automata, int automatonIndex)
    {
        for (int index = 0; index < automata.Count; index++)
        {
            if (automata[index] == automatonIndex)
            {
                return;
            }
        }

        automata.Add(automatonIndex);
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
