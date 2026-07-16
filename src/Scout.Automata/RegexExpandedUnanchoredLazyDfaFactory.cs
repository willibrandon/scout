namespace Scout;

/// <summary>
/// Defers expanded byte-NFA construction until a generic unanchored runner is required.
/// </summary>
/// <param name="root">The parsed regex root.</param>
/// <param name="options">The root compilation options.</param>
/// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
internal sealed class RegexExpandedUnanchoredLazyDfaFactory(
    RegexSyntaxNode root,
    RegexCompileOptions options,
    ulong dfaSizeLimit)
{
    private readonly Lazy<RegexUnanchoredLazyDfaFactory?> _factory = new(
        () => CreateFactory(root, options, dfaSizeLimit),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets a value indicating whether the one-time initialization permanently rejected construction.
    /// </summary>
    internal bool IsPermanentlyRejected => _factory.IsValueCreated && _factory.Value is null;

    /// <summary>
    /// Creates an independent runner from the lazily compiled shared byte NFAs.
    /// </summary>
    /// <returns>The runner, or <see langword="null" /> when expanded construction is ineligible.</returns>
    internal RegexUnanchoredLazyDfa? Create()
    {
        return _factory.Value?.Create();
    }

    private static RegexUnanchoredLazyDfaFactory? CreateFactory(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        ulong dfaSizeLimit)
    {
        if (!RegexUnanchoredLazyDfa.CanCompileExpandedForwardNfaWithinBudget(
                root,
                options,
                dfaSizeLimit))
        {
            return null;
        }

        var constructionBudget = new RegexNfaConstructionBudget(dfaSizeLimit);
        if (!RegexNfaCompiler.TryCompileUnanchored(
                root,
                options,
                constructionBudget,
                out RegexNfa? forwardNfa) ||
            !RegexDfaOperations.CanCompile(forwardNfa!))
        {
            return null;
        }

        return new RegexUnanchoredLazyDfaFactory(
            forwardNfa!,
            root,
            options,
            dfaSizeLimit,
            constructionBudget,
            forwardNfaIsUnanchored: true);
    }
}
