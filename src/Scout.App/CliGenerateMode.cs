namespace Scout;

/// <summary>
/// Identifies generated artifact modes requested by <c>--generate</c>.
/// </summary>
public enum CliGenerateMode
{
    /// <summary>
    /// Generate the roff manual page.
    /// </summary>
    Man,

    /// <summary>
    /// Generate Bash shell completions.
    /// </summary>
    CompleteBash,

    /// <summary>
    /// Generate Zsh shell completions.
    /// </summary>
    CompleteZsh,

    /// <summary>
    /// Generate Fish shell completions.
    /// </summary>
    CompleteFish,

    /// <summary>
    /// Generate PowerShell completions.
    /// </summary>
    CompletePowerShell,
}
