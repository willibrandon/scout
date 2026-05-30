namespace Scout;

internal enum RegexEngineKind
{
    PikeVm,
    BoundedBacktracker,
    OnePassDfa,
    DenseDfa,
    SparseDfa,
    LazyDfa,
}
