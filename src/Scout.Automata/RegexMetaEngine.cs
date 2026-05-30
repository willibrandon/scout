using System;

namespace Scout;

internal sealed class RegexMetaEngine
{
    private const int DenseDfaNfaStateLimit = 8;
    private const int SparseDfaNfaStateLimit = 32;
    private const int DenseDfaStateLimit = 16;
    private const int SparseDfaStateLimit = 64;
    private const int BoundedBacktrackerNfaStateLimit = 24;

    private readonly PikeVm? pikeVm;
    private readonly RegexBoundedBacktracker? boundedBacktracker;
    private readonly RegexDenseDfa? denseDfa;
    private readonly RegexSparseDfa? sparseDfa;
    private readonly RegexLazyDfa? lazyDfa;

    private RegexMetaEngine(
        RegexEngineKind kind,
        PikeVm? pikeVm,
        RegexBoundedBacktracker? boundedBacktracker,
        RegexDenseDfa? denseDfa,
        RegexSparseDfa? sparseDfa,
        RegexLazyDfa? lazyDfa)
    {
        Kind = kind;
        this.pikeVm = pikeVm;
        this.boundedBacktracker = boundedBacktracker;
        this.denseDfa = denseDfa;
        this.sparseDfa = sparseDfa;
        this.lazyDfa = lazyDfa;
    }

    public RegexEngineKind Kind { get; }

    public static RegexMetaEngine Compile(RegexNfa nfa)
    {
        if (!RegexDfaOperations.CanCompile(nfa))
        {
            if (nfa.States.Count <= BoundedBacktrackerNfaStateLimit && RegexBoundedBacktracker.CanCompile(nfa))
            {
                return new RegexMetaEngine(
                    RegexEngineKind.BoundedBacktracker,
                    pikeVm: null,
                    boundedBacktracker: new RegexBoundedBacktracker(nfa),
                    denseDfa: null,
                    sparseDfa: null,
                    lazyDfa: null);
            }

            return new RegexMetaEngine(
                RegexEngineKind.PikeVm,
                new PikeVm(nfa),
                boundedBacktracker: null,
                denseDfa: null,
                sparseDfa: null,
                lazyDfa: null);
        }

        if (nfa.States.Count <= DenseDfaNfaStateLimit &&
            RegexDenseDfa.TryCompile(nfa, DenseDfaStateLimit, out RegexDenseDfa? denseDfa))
        {
            return new RegexMetaEngine(
                RegexEngineKind.DenseDfa,
                pikeVm: null,
                boundedBacktracker: null,
                denseDfa: denseDfa,
                sparseDfa: null,
                lazyDfa: null);
        }

        if (nfa.States.Count <= SparseDfaNfaStateLimit &&
            RegexSparseDfa.TryCompile(nfa, SparseDfaStateLimit, out RegexSparseDfa? sparseDfa))
        {
            return new RegexMetaEngine(
                RegexEngineKind.SparseDfa,
                pikeVm: null,
                boundedBacktracker: null,
                denseDfa: null,
                sparseDfa: sparseDfa,
                lazyDfa: null);
        }

        return new RegexMetaEngine(
            RegexEngineKind.LazyDfa,
            pikeVm: null,
            boundedBacktracker: null,
            denseDfa: null,
            sparseDfa: null,
            lazyDfa: new RegexLazyDfa(nfa));
    }

    public RegexMatch? Find(ReadOnlySpan<byte> haystack, int startAt)
    {
        int startOffset = Math.Clamp(startAt, 0, haystack.Length);
        for (int start = startOffset; start <= haystack.Length; start++)
        {
            if (TryMatchAt(haystack, start, out int length))
            {
                return new RegexMatch(start, length);
            }
        }

        return null;
    }

    private bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length)
    {
        if (lazyDfa is not null)
        {
            return lazyDfa.TryMatchAt(haystack, start, out length);
        }

        if (sparseDfa is not null)
        {
            return sparseDfa.TryMatchAt(haystack, start, out length);
        }

        if (denseDfa is not null)
        {
            return denseDfa.TryMatchAt(haystack, start, out length);
        }

        if (boundedBacktracker is not null)
        {
            return boundedBacktracker.TryMatchAt(haystack, start, out length);
        }

        return pikeVm!.TryMatchAt(haystack, start, out length);
    }
}
