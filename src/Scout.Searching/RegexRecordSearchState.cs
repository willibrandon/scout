namespace Scout;

/// <summary>
/// Retains the runners, mutable sink, and accumulated results of a projected record search.
/// </summary>
internal ref struct RegexRecordSearchState<TSink> where TSink : struct, ILineSink
{
    internal RegexMatchEndRunner ProjectedRunner;

    internal RegexFindRunner AuthoritativeRunner;

    internal TSink Sink;

    internal bool UseProjection;

    internal bool Matched;

    internal ulong MatchedLines;

    internal long Matches;
}
