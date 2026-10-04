namespace Scout;

/// <summary>
/// Tracks non-overlapping literal matches during a counting scan.
/// </summary>
internal struct RegexLiteralSetCountState
{
    internal long Total;

    internal int NextAllowedStart;
}
