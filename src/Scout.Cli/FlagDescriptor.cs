using System;

namespace Scout;

/// <summary>
/// Describes one ripgrep-compatible command-line flag definition.
/// </summary>
internal sealed class FlagDescriptor
{
    private readonly Func<CliLowArgs, ScoutError?>? applySwitch;

    private FlagDescriptor(
        string longName,
        char? shortName,
        string? negatedName,
        string[] aliases,
        FlagKind kind,
        FlagCategory category,
        string doc,
        Func<CliLowArgs, ScoutError?>? applySwitch)
    {
        LongName = longName;
        ShortName = shortName;
        NegatedName = negatedName;
        Aliases = aliases;
        Kind = kind;
        Category = category;
        Doc = doc;
        this.applySwitch = applySwitch;
    }

    /// <summary>
    /// Gets the canonical long flag name, including the leading <c>--</c>.
    /// </summary>
    public string LongName { get; }

    /// <summary>
    /// Gets the canonical short flag name without the leading <c>-</c>, if one exists.
    /// </summary>
    public char? ShortName { get; }

    /// <summary>
    /// Gets the canonical negated long flag name, if one exists.
    /// </summary>
    public string? NegatedName { get; }

    /// <summary>
    /// Gets alternate long flag spellings.
    /// </summary>
    public string[] Aliases { get; }

    /// <summary>
    /// Gets the parser shape for this flag.
    /// </summary>
    public FlagKind Kind { get; }

    /// <summary>
    /// Gets the help and completion category for this flag.
    /// </summary>
    public FlagCategory Category { get; }

    /// <summary>
    /// Gets the short documentation text for this flag.
    /// </summary>
    public string Doc { get; }

    /// <summary>
    /// Creates a no-value switch flag descriptor.
    /// </summary>
    /// <param name="longName">The canonical long flag name, including <c>--</c>.</param>
    /// <param name="shortName">The short flag name without <c>-</c>, or <see langword="null" />.</param>
    /// <param name="negatedName">The negated long flag name, or <see langword="null" />.</param>
    /// <param name="aliases">Alternate long flag spellings.</param>
    /// <param name="category">The help and completion category.</param>
    /// <param name="doc">The short documentation text.</param>
    /// <param name="applySwitch">The parser action for this switch.</param>
    /// <returns>The descriptor.</returns>
    public static FlagDescriptor Switch(
        string longName,
        char? shortName,
        string? negatedName,
        string[] aliases,
        FlagCategory category,
        string doc,
        Func<CliLowArgs, ScoutError?> applySwitch)
    {
        ArgumentNullException.ThrowIfNull(longName);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(applySwitch);

        return new FlagDescriptor(longName, shortName, negatedName, aliases, FlagKind.Switch, category, doc, applySwitch);
    }

    /// <summary>
    /// Determines whether the descriptor recognizes the given long spelling.
    /// </summary>
    /// <param name="name">The long flag spelling.</param>
    /// <returns><see langword="true" /> when the name matches this descriptor.</returns>
    public bool MatchesLongName(string name)
    {
        if (string.Equals(LongName, name, StringComparison.Ordinal))
        {
            return true;
        }

        for (int index = 0; index < Aliases.Length; index++)
        {
            if (string.Equals(Aliases[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies this switch flag to the low-level argument state.
    /// </summary>
    /// <param name="lowArgs">The low-level argument state.</param>
    /// <param name="error">The parsing error, if any.</param>
    /// <returns><see langword="true" /> when the switch action ran.</returns>
    public bool TryApplySwitch(CliLowArgs lowArgs, out ScoutError? error)
    {
        if (Kind != FlagKind.Switch || applySwitch is null)
        {
            error = null;
            return false;
        }

        error = applySwitch(lowArgs);
        return true;
    }
}
