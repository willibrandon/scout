namespace Scout;

internal enum RegexEngineKind
{
    LiteralSet,
    AlternationSet,
    SimpleSequence,
    PikeVm,
    BoundedBacktracker,
    OnePassDfa,
    DenseDfa,
    SparseDfa,
    LazyDfa,
}
