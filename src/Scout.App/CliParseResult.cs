
namespace Scout;

/// <summary>
/// Represents the result of low-level command-line parsing.
/// </summary>
public sealed class CliParseResult
{
    private CliParseResult(CliParseStatus status, CliLowArgs? lowArgs, CliSpecialMode specialMode, ScoutError? error)
    {
        Status = status;
        LowArgs = lowArgs;
        SpecialMode = specialMode;
        Error = error;
    }

    /// <summary>
    /// Gets the parse result status.
    /// </summary>
    public CliParseStatus Status { get; }

    /// <summary>
    /// Gets the low-level arguments when <see cref="Status" /> is <see cref="CliParseStatus.Ok" />.
    /// </summary>
    public CliLowArgs? LowArgs { get; }

    /// <summary>
    /// Gets the special mode when <see cref="Status" /> is <see cref="CliParseStatus.Special" />.
    /// </summary>
    public CliSpecialMode SpecialMode { get; }

    /// <summary>
    /// Gets the parse error when <see cref="Status" /> is <see cref="CliParseStatus.Error" />.
    /// </summary>
    public ScoutError? Error { get; }

    /// <summary>
    /// Creates a successful parse result.
    /// </summary>
    /// <param name="lowArgs">The parsed low-level arguments.</param>
    /// <returns>A successful parse result.</returns>
    public static CliParseResult Ok(CliLowArgs lowArgs)
    {
        ArgumentNullException.ThrowIfNull(lowArgs);
        return new CliParseResult(CliParseStatus.Ok, lowArgs, default, error: null);
    }

    /// <summary>
    /// Creates a special-mode parse result.
    /// </summary>
    /// <param name="specialMode">The special mode to execute.</param>
    /// <returns>A special-mode parse result.</returns>
    public static CliParseResult Special(CliSpecialMode specialMode)
    {
        return new CliParseResult(CliParseStatus.Special, lowArgs: null, specialMode, error: null);
    }

    /// <summary>
    /// Creates a failed parse result.
    /// </summary>
    /// <param name="error">The parse error.</param>
    /// <returns>A failed parse result.</returns>
    public static CliParseResult Fail(ScoutError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CliParseResult(CliParseStatus.Error, lowArgs: null, default, error);
    }
}
