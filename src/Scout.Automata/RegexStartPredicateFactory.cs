namespace Scout;

/// <summary>
/// Defers construction of a Unicode-aware start predicate until a fallback path needs it.
/// </summary>
/// <param name="root">The parsed regex root.</param>
/// <param name="options">The root compilation options.</param>
/// <param name="prefixSet">The optional exact prefix set.</param>
internal sealed class RegexStartPredicateFactory(
    RegexSyntaxNode root,
    RegexCompileOptions options,
    RegexStartPrefixSet? prefixSet)
{
    private readonly Lazy<RegexStartPredicate?> _predicate = new(
        () => RegexStartPredicate.TryCreate(
            root,
            options,
            prefixSet,
            out RegexStartPredicate? predicate)
                ? predicate
                : null,
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets the shared predicate, constructing it once on first demand.
    /// </summary>
    /// <returns>The predicate, or <see langword="null" /> when syntax cannot narrow starts.</returns>
    internal RegexStartPredicate? GetOrCreate()
    {
        return _predicate.Value;
    }
}
