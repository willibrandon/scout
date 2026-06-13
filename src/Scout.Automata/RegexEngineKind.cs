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
    RunLiteralDotStar,
    UnicodeLetterLiteralRun,
    WordBoundaryLiteralSet,
    SimpleSequence,
    EndAnchoredSequence,
    LineContains,
    DotStarClassFallback,
    PikeVm,
    BoundedBacktracker,
    OnePassDfa,
    DenseDfa,
    SparseDfa,
    LazyDfa,
}
