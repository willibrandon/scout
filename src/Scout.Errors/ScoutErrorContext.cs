namespace Scout;

/// <summary>
/// Provides Scout-owned top-level diagnostic context labels.
/// </summary>
public static class ScoutErrorContext
{
    /// <summary>
    /// Gets the program name used in user-facing diagnostics.
    /// </summary>
    public const string ProgramName = "scout";

    /// <summary>
    /// Gets the top-level program context.
    /// </summary>
    /// <returns>The program context string.</returns>
    public static string ProgramContext()
    {
        return ProgramName;
    }

    /// <summary>
    /// Gets the top-level program and path context.
    /// </summary>
    /// <param name="path">The displayed path.</param>
    /// <returns>The program and path context string.</returns>
    public static string ProgramPathContext(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ProgramName + ": " + path;
    }
}
