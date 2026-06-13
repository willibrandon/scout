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
    LiteralPrefixRun,
    BoundedLiteralGap,
    UnicodeLetterLiteralRun,
    WordBoundaryLiteralSet,
    WordSuffixLiteral,
    SimpleSequence,
    EndAnchoredSequence,
    EndAnchoredAtom,
    LineContains,
    DotStarClassFallback,
    PikeVm,
    BoundedBacktracker,
    OnePassDfa,
    DenseDfa,
    SparseDfa,
    LazyDfa,
}
