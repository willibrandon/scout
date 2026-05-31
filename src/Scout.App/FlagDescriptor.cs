using System;

namespace Scout;

/// <summary>
/// Describes one ripgrep-compatible command-line flag definition.
/// </summary>
internal sealed class FlagDescriptor
{
    private readonly Func<CliLowArgs, string, ScoutError?>? applySwitch;
    private readonly Func<CliLowArgs, OsString, string, ScoutError?>? applyValue;
    private readonly Func<string, CliSpecialMode>? selectSpecialMode;

    private FlagDescriptor(
        string longName,
        char? shortName,
        string? negatedName,
        string[] aliases,
        FlagKind kind,
        FlagCategory category,
        string doc,
        Func<CliLowArgs, string, ScoutError?>? applySwitch,
        Func<CliLowArgs, OsString, string, ScoutError?>? applyValue,
        Func<string, CliSpecialMode>? selectSpecialMode)
    {
        LongName = longName;
        ShortName = shortName;
        NegatedName = negatedName;
        Aliases = aliases;
        Kind = kind;
        Category = category;
        Doc = doc;
        this.applySwitch = applySwitch;
        this.applyValue = applyValue;
        this.selectSpecialMode = selectSpecialMode;
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

        return new FlagDescriptor(longName, shortName, negatedName, aliases, FlagKind.Switch, category, doc, (lowArgs, _) => applySwitch(lowArgs), applyValue: null, selectSpecialMode: null);
    }

    /// <summary>
    /// Creates a no-value switch flag descriptor whose parser action needs the matched spelling.
    /// </summary>
    /// <param name="longName">The canonical long flag name, including <c>--</c>.</param>
    /// <param name="shortName">The short flag name without <c>-</c>, or <see langword="null" />.</param>
    /// <param name="negatedName">The negated long flag name, or <see langword="null" />.</param>
    /// <param name="aliases">Alternate long flag spellings.</param>
    /// <param name="category">The help and completion category.</param>
    /// <param name="doc">The short documentation text.</param>
    /// <param name="applySwitch">The parser action for this switch.</param>
    /// <returns>The descriptor.</returns>
    public static FlagDescriptor SwitchWithName(
        string longName,
        char? shortName,
        string? negatedName,
        string[] aliases,
        FlagCategory category,
        string doc,
        Func<CliLowArgs, string, ScoutError?> applySwitch)
    {
        ArgumentNullException.ThrowIfNull(longName);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(applySwitch);

        return new FlagDescriptor(longName, shortName, negatedName, aliases, FlagKind.Switch, category, doc, applySwitch, applyValue: null, selectSpecialMode: null);
    }

    /// <summary>
    /// Creates a required-value flag descriptor.
    /// </summary>
    /// <param name="longName">The canonical long flag name, including <c>--</c>.</param>
    /// <param name="shortName">The short flag name without <c>-</c>, or <see langword="null" />.</param>
    /// <param name="aliases">Alternate long flag spellings.</param>
    /// <param name="category">The help and completion category.</param>
    /// <param name="doc">The short documentation text.</param>
    /// <param name="applyValue">The parser action for this value flag.</param>
    /// <returns>The descriptor.</returns>
    public static FlagDescriptor Value(
        string longName,
        char? shortName,
        string[] aliases,
        FlagCategory category,
        string doc,
        Func<CliLowArgs, OsString, string, ScoutError?> applyValue)
    {
        ArgumentNullException.ThrowIfNull(longName);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(applyValue);

        return new FlagDescriptor(longName, shortName, negatedName: null, aliases, FlagKind.Value, category, doc, applySwitch: null, applyValue, selectSpecialMode: null);
    }

    /// <summary>
    /// Creates a special-mode flag descriptor.
    /// </summary>
    /// <param name="longName">The canonical long flag name, including <c>--</c>.</param>
    /// <param name="shortName">The short flag name without <c>-</c>, or <see langword="null" />.</param>
    /// <param name="aliases">Alternate long flag spellings.</param>
    /// <param name="category">The help and completion category.</param>
    /// <param name="doc">The short documentation text.</param>
    /// <param name="selectSpecialMode">Selects the special mode for the matched spelling.</param>
    /// <returns>The descriptor.</returns>
    public static FlagDescriptor Special(
        string longName,
        char? shortName,
        string[] aliases,
        FlagCategory category,
        string doc,
        Func<string, CliSpecialMode> selectSpecialMode)
    {
        ArgumentNullException.ThrowIfNull(longName);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(selectSpecialMode);

        return new FlagDescriptor(longName, shortName, negatedName: null, aliases, FlagKind.Special, category, doc, applySwitch: null, applyValue: null, selectSpecialMode);
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
    /// Determines whether the descriptor recognizes the given negated long spelling.
    /// </summary>
    /// <param name="name">The long flag spelling.</param>
    /// <returns><see langword="true" /> when the name matches this descriptor's negated spelling.</returns>
    public bool MatchesNegatedName(string name)
    {
        return NegatedName is not null && string.Equals(NegatedName, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies this switch flag to the low-level argument state.
    /// </summary>
    /// <param name="lowArgs">The low-level argument state.</param>
    /// <param name="matchedName">The spelling that matched this switch.</param>
    /// <param name="error">The parsing error, if any.</param>
    /// <returns><see langword="true" /> when the switch action ran.</returns>
    public bool TryApplySwitch(CliLowArgs lowArgs, string matchedName, out ScoutError? error)
    {
        if (Kind != FlagKind.Switch || applySwitch is null)
        {
            error = null;
            return false;
        }

        error = applySwitch(lowArgs, matchedName);
        return true;
    }

    /// <summary>
    /// Applies this required-value flag to the low-level argument state.
    /// </summary>
    /// <param name="lowArgs">The low-level argument state.</param>
    /// <param name="value">The parsed flag value.</param>
    /// <param name="matchedName">The spelling that matched this value flag.</param>
    /// <param name="error">The parsing error, if any.</param>
    /// <returns><see langword="true" /> when the value action ran.</returns>
    public bool TryApplyValue(CliLowArgs lowArgs, OsString value, string matchedName, out ScoutError? error)
    {
        if (Kind != FlagKind.Value || applyValue is null)
        {
            error = null;
            return false;
        }

        error = applyValue(lowArgs, value, matchedName);
        return true;
    }

    /// <summary>
    /// Selects the special mode for this flag.
    /// </summary>
    /// <param name="matchedName">The spelling that matched this special flag.</param>
    /// <param name="mode">The selected special mode.</param>
    /// <returns><see langword="true" /> when this descriptor represents a special mode.</returns>
    public bool TryGetSpecialMode(string matchedName, out CliSpecialMode mode)
    {
        if (Kind != FlagKind.Special || selectSpecialMode is null)
        {
            mode = default;
            return false;
        }

        mode = selectSpecialMode(matchedName);
        return true;
    }
}
