using System.Runtime.CompilerServices;

namespace Scout;

/// <summary>
/// Emits Scout debug and trace diagnostics with stable category and source-location fields.
/// </summary>
public readonly struct DiagnosticLogger : IEquatable<DiagnosticLogger>
{
    private readonly DiagnosticMessenger diagnostics;
    private readonly CliLoggingMode? mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticLogger" /> struct.
    /// </summary>
    /// <param name="diagnostics">The diagnostic messenger to write to.</param>
    /// <param name="mode">The requested diagnostic logging mode.</param>
    public DiagnosticLogger(DiagnosticMessenger diagnostics, CliLoggingMode? mode)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
        this.mode = mode;
    }

    /// <summary>
    /// Gets a value indicating whether debug-level diagnostics should be emitted.
    /// </summary>
    public bool IsDebugEnabled => mode is CliLoggingMode.Debug or CliLoggingMode.Trace;

    /// <summary>
    /// Gets a value indicating whether trace-level diagnostics should be emitted.
    /// </summary>
    public bool IsTraceEnabled => mode == CliLoggingMode.Trace;

    /// <summary>
    /// Tests whether two diagnostic loggers target the same messenger and mode.
    /// </summary>
    /// <param name="left">The left logger.</param>
    /// <param name="right">The right logger.</param>
    /// <returns><see langword="true" /> when the loggers are equal.</returns>
    public static bool operator ==(DiagnosticLogger left, DiagnosticLogger right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Tests whether two diagnostic loggers differ.
    /// </summary>
    /// <param name="left">The left logger.</param>
    /// <param name="right">The right logger.</param>
    /// <returns><see langword="true" /> when the loggers are not equal.</returns>
    public static bool operator !=(DiagnosticLogger left, DiagnosticLogger right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(DiagnosticLogger other)
    {
        return ReferenceEquals(diagnostics, other.diagnostics) && mode == other.mode;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is DiagnosticLogger other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(diagnostics, mode);
    }

    /// <summary>
    /// Emits a debug-level diagnostic message.
    /// </summary>
    /// <param name="target">The stable Scout diagnostic category.</param>
    /// <param name="file">The stable repo-relative Scout source path.</param>
    /// <param name="message">The diagnostic message body.</param>
    /// <param name="line">The source line number of the logging call.</param>
    public void Debug(string target, string file, string message, [CallerLineNumber] int line = 0)
    {
        if (IsDebugEnabled)
        {
            Log("DEBUG", target, file, line, message);
        }
    }

    /// <summary>
    /// Emits a trace-level diagnostic message.
    /// </summary>
    /// <param name="target">The stable Scout diagnostic category.</param>
    /// <param name="file">The stable repo-relative Scout source path.</param>
    /// <param name="message">The diagnostic message body.</param>
    /// <param name="line">The source line number of the logging call.</param>
    public void Trace(string target, string file, string message, [CallerLineNumber] int line = 0)
    {
        if (IsTraceEnabled)
        {
            Log("TRACE", target, file, line, message);
        }
    }

    private void Log(string level, string target, string file, int line, string message)
    {
        diagnostics.Message($"{ScoutErrorContext.ProgramName}: {level}|{target}|{FormatSourcePath(file)}:{line}: {message}");
    }

    private static string FormatSourcePath(string file)
    {
        return OperatingSystem.IsWindows()
            ? file.Replace('/', '\\')
            : file.Replace('\\', '/');
    }
}
