namespace Scout;

/// <summary>
/// Owns the selected compiled regex engine and prepared patterns for one search operation.
/// </summary>
/// <param name="patterns">The patterns prepared for the selected engine.</param>
/// <param name="nativePlan">The selected native plan, or <see langword="null" />.</param>
/// <param name="pcre2Plan">The selected PCRE2 plan, or <see langword="null" />.</param>
/// <param name="asciiCaseInsensitive">Whether native matching is ASCII case-insensitive.</param>
internal sealed class RegexEnginePlan(
    List<byte[]> patterns,
    RegexSearchPlan? nativePlan,
    Pcre2SearchPlan? pcre2Plan,
    bool asciiCaseInsensitive) : IDisposable
{
    private readonly List<byte[]> _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    private readonly RegexSearchPlan? _nativePlan = ValidateSelectedPlan(nativePlan, pcre2Plan);
    private readonly Pcre2SearchPlan? _pcre2Plan = pcre2Plan;
    private readonly bool _asciiCaseInsensitive = asciiCaseInsensitive;

    /// <summary>
    /// Gets the patterns prepared for the selected engine.
    /// </summary>
    internal List<byte[]> Patterns => _patterns;

    /// <summary>
    /// Gets the selected native plan, or <see langword="null" /> when PCRE2 was selected.
    /// </summary>
    internal RegexSearchPlan? NativePlan => _nativePlan;

    /// <summary>
    /// Gets the selected PCRE2 plan, or <see langword="null" /> when the native engine was selected.
    /// </summary>
    internal Pcre2SearchPlan? Pcre2Plan => _pcre2Plan;

    /// <summary>
    /// Gets a value indicating whether PCRE2 was selected.
    /// </summary>
    internal bool UsesPcre2 => _pcre2Plan is not null;

    /// <summary>
    /// Gets a value indicating whether native matching is ASCII case-insensitive.
    /// </summary>
    internal bool AsciiCaseInsensitive => _asciiCaseInsensitive;

    /// <inheritdoc />
    public void Dispose()
    {
        _pcre2Plan?.Dispose();
    }

    private static RegexSearchPlan? ValidateSelectedPlan(
        RegexSearchPlan? nativePlan,
        Pcre2SearchPlan? pcre2Plan)
    {
        if ((nativePlan is null) == (pcre2Plan is null))
        {
            throw new ArgumentException("A regex engine plan must select exactly one compiled matcher.");
        }

        return nativePlan;
    }
}
