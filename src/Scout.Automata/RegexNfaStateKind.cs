namespace Scout;

internal enum RegexNfaStateKind
{
    Atom,
    Predicate,
    Split,
    GreedyLoopSplit,
    LazyLoopSplit,
    Accept,
}
