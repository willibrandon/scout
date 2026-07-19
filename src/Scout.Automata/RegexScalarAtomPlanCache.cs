namespace Scout;

/// <summary>
/// Lowers each authoritative scalar atom once per effective option scope.
/// </summary>
internal sealed class RegexScalarAtomPlanCache()
{
    private Dictionary<RegexScalarAtomPlanCacheKey, RegexScalarAtomPlan?>? _plans;

    /// <summary>
    /// Gets or creates the immutable scalar plan for an atom.
    /// </summary>
    /// <param name="atom">The parsed authoritative atom.</param>
    /// <param name="options">The effective compile options.</param>
    /// <param name="plan">Receives the scalar plan when lowering succeeds.</param>
    /// <returns><see langword="true" /> when the atom has a scalar-set representation.</returns>
    internal bool TryGet(
        RegexAtomNode atom,
        RegexCompileOptions options,
        out RegexScalarAtomPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(atom);

        var key = RegexScalarAtomPlanCacheKey.Create(atom, options);
        if (_plans is not null && _plans.TryGetValue(key, out plan))
        {
            return plan is not null;
        }

        if (!RegexUtf8ByteCompiler.TryBuildNormalizedScalarRanges(
                atom,
                options,
                out List<RegexScalarRange> ranges))
        {
            (_plans ??= []).Add(key, null);
            plan = null;
            return false;
        }

        plan = new RegexScalarAtomPlan([.. ranges]);
        (_plans ??= []).Add(key, plan);
        return true;
    }
}
