
namespace Scout.IO.Ignore;

/// <summary>
/// Builds explicit override glob matchers.
/// </summary>
public sealed class OverrideBuilder
{
    private readonly string baseDirectory;
    private readonly List<OverrideRule> rules = [];
    private bool hasWhitelist;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideBuilder" /> class.
    /// </summary>
    /// <param name="baseDirectory">The directory that override globs are matched relative to.</param>
    public OverrideBuilder(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        this.baseDirectory = baseDirectory;
    }

    /// <summary>
    /// Adds an override glob.
    /// </summary>
    /// <param name="pattern">The override glob. Leading <c>!</c> means ignore; otherwise it means whitelist.</param>
    /// <param name="asciiCaseInsensitive">Whether ASCII byte case should be ignored.</param>
    /// <returns>This builder.</returns>
    public OverrideBuilder Add(string pattern, bool asciiCaseInsensitive = false)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (OverrideRule.TryParse(baseDirectory, pattern, asciiCaseInsensitive, out OverrideRule? rule) && rule is not null)
        {
            hasWhitelist |= rule.Decision == IgnoreDecision.Whitelist;
            rules.Add(rule);
        }

        return this;
    }

    /// <summary>
    /// Builds an immutable override matcher.
    /// </summary>
    /// <returns>An override matcher.</returns>
    public Override Build()
    {
        return new Override(rules.ToArray(), hasWhitelist);
    }
}
