
namespace Scout;

/// <summary>
/// Matches explicit override globs with ripgrep override precedence.
/// </summary>
public sealed class Override
{
    private readonly OverrideRule[] rules;
    private readonly bool hasWhitelist;

    internal Override(OverrideRule[] rules, bool hasWhitelist)
    {
        this.rules = rules;
        this.hasWhitelist = hasWhitelist;
    }

    /// <summary>
    /// Gets an empty override matcher.
    /// </summary>
    public static Override Empty { get; } = new([], hasWhitelist: false);

    /// <summary>
    /// Gets a value indicating whether this override matcher has no rules.
    /// </summary>
    public bool IsEmpty => rules.Length == 0;

    /// <summary>
    /// Tests whether the override rules ignore the given path.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="isDirectory">Whether the path represents a directory.</param>
    /// <returns><see langword="true" /> when the path is ignored by these override rules.</returns>
    public bool IsIgnored(string path, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        FileAttributes attributes = isDirectory ? FileAttributes.Directory : default;
        var entry = new DirEntry(
            Path.GetFullPath(path),
            depth: 0,
            attributes,
            isDirectory,
            isSymbolicLink: false,
            isStdin: false,
            length: null,
            identity: default);
        return Match(entry) == IgnoreDecision.Ignore;
    }

    internal IgnoreDecision Match(DirEntry entry)
    {
        if (rules.Length == 0)
        {
            return IgnoreDecision.None;
        }

        IgnoreDecision decision = IgnoreDecision.None;
        for (int index = 0; index < rules.Length; index++)
        {
            IgnoreDecision current = rules[index].Match(entry);
            if (current != IgnoreDecision.None)
            {
                decision = current;
            }
        }

        if (decision == IgnoreDecision.None && hasWhitelist && !entry.IsDirectory)
        {
            return IgnoreDecision.Ignore;
        }

        return decision;
    }
}
