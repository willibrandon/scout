namespace Scout;

/// <summary>
/// Contains process exit-code constants used by Scout.
/// </summary>
public static class ExitCode
{
    /// <summary>
    /// The command completed successfully.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// The command completed without finding a match.
    /// </summary>
    public const int NoMatch = 1;

    /// <summary>
    /// The command encountered an error.
    /// </summary>
    public const int Error = 2;
}
