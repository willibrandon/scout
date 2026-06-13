namespace Scout;

internal enum RegexEngineKind
{
    LiteralSet,
    AlternationSet,
    DotStar,
    Ipv4Address,
    EmailAddress,
    Uri,
    WordWhitespaceLiteral,
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
