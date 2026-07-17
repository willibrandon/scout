namespace Scout;

/// <summary>
/// Compiles the authoritative native regex plan shared by one command-line search operation.
/// </summary>
internal static class NativeRegexSearchPlanFactory
{
    /// <summary>
    /// Compiles the plan whose record semantics match the selected standard or JSON execution path.
    /// </summary>
    /// <param name="patterns">The prepared ordered pattern set.</param>
    /// <param name="lowArgs">The parsed low-level command-line arguments.</param>
    /// <param name="asciiCaseInsensitive">Whether matching is ASCII case-insensitive.</param>
    /// <returns>The compiled operation-scoped plan.</returns>
    internal static RegexSearchPlan Create(
        IReadOnlyList<byte[]> patterns,
        CliLowArgs lowArgs,
        bool asciiCaseInsensitive)
    {
        bool preserveCrlfCarriageReturn = lowArgs.Multiline &&
            lowArgs.Crlf &&
            !lowArgs.NullData;
        return RegexSearchPlan.CreateScoped(
            patterns,
            new RegexSearchPlanOptions(
                asciiCaseInsensitive,
                lowArgs.LineRegexp,
                lowArgs.WordRegexp,
                lowArgs.Crlf,
                lowArgs.NullData,
                multiline: false,
                multilineDotall: lowArgs.MultilineDotall,
                preserveCrlfCarriageReturn),
            GetScopePolicy(lowArgs),
            lowArgs.DfaSizeLimit);
    }

    private static RegexSearchScopePolicy GetScopePolicy(CliLowArgs lowArgs)
    {
        if (!lowArgs.Multiline)
        {
            return RegexSearchScopePolicy.Records;
        }

        if (lowArgs.SearchMode != CliSearchMode.Json)
        {
            return lowArgs.NullData
                ? RegexSearchScopePolicy.Records
                : RegexSearchScopePolicy.StandardMultiline;
        }

        bool nullDataContext = lowArgs.NullData &&
            (lowArgs.BeforeContext > 0 || lowArgs.AfterContext > 0 || lowArgs.Passthru);
        return nullDataContext
            ? RegexSearchScopePolicy.Records
            : RegexSearchScopePolicy.JsonMultiline;
    }
}
