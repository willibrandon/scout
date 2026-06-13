namespace Scout;

internal enum RegexEngineKind
{
    LiteralSet,
    AlternationSet,
    DotStar,
    Ipv4Address,
    SimpleSequence,
    LineContains,
    DotStarClassFallback,
    PikeVm,
    BoundedBacktracker,
    OnePassDfa,
    DenseDfa,
    SparseDfa,
    LazyDfa,
}
