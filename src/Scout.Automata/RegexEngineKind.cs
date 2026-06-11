namespace Scout;

internal enum RegexEngineKind
{
    LiteralSet,
    AlternationSet,
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
