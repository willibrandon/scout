using System;

namespace Scout;

internal sealed class RegexMetaEngine
{
    private readonly PikeVm? pikeVm;
    private readonly RegexLazyDfa? lazyDfa;

    private RegexMetaEngine(RegexEngineKind kind, PikeVm? pikeVm, RegexLazyDfa? lazyDfa)
    {
        Kind = kind;
        this.pikeVm = pikeVm;
        this.lazyDfa = lazyDfa;
    }

    public RegexEngineKind Kind { get; }

    public static RegexMetaEngine Compile(RegexNfa nfa)
    {
        if (RegexLazyDfa.CanCompile(nfa))
        {
            return new RegexMetaEngine(RegexEngineKind.LazyDfa, pikeVm: null, new RegexLazyDfa(nfa));
        }

        return new RegexMetaEngine(RegexEngineKind.PikeVm, new PikeVm(nfa), lazyDfa: null);
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

        return pikeVm!.TryMatchAt(haystack, start, out length);
    }
}
