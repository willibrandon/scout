namespace Scout;

internal enum RegexEngineKind
{
    LiteralSet,
    AlternationSet,
    DotStar,
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
