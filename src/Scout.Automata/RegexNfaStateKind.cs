namespace Scout;

internal enum RegexNfaStateKind
{
    Atom,
    Predicate,
    CaptureStart,
    CaptureEnd,
    Split,
    GreedyLoopSplit,
    LazyLoopSplit,
    Accept,
}
