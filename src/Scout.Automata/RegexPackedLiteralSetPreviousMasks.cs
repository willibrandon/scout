namespace Scout;

/// <summary>
/// Carries the three previous SIMD masks across adjacent chunks.
/// </summary>
internal struct RegexPackedLiteralSetPreviousMasks<TVector> where TVector : struct
{
    internal TVector First;

    internal TVector Second;

    internal TVector Third;
}
