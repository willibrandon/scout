namespace Scout;

/// <summary>
/// Accumulates match counts and matched byte lengths.
/// </summary>
internal struct RegexMatchTotals
{
    internal long Count;

    internal long SpanSum;
}
