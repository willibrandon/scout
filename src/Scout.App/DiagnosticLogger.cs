using System;

namespace Scout;

internal readonly struct DiagnosticLogger
{
    private readonly DiagnosticMessenger diagnostics;
    private readonly CliLoggingMode? mode;

    public DiagnosticLogger(DiagnosticMessenger diagnostics, CliLoggingMode? mode)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
        this.mode = mode;
    }

    public bool IsDebugEnabled => mode is CliLoggingMode.Debug or CliLoggingMode.Trace;

    public bool IsTraceEnabled => mode == CliLoggingMode.Trace;

    public void Debug(string target, string file, int line, string message)
    {
        if (IsDebugEnabled)
        {
            Log("DEBUG", target, file, line, message);
        }
    }

    public void Trace(string target, string file, int line, string message)
    {
        if (IsTraceEnabled)
        {
            Log("TRACE", target, file, line, message);
        }
    }

    private void Log(string level, string target, string file, int line, string message)
    {
        diagnostics.Message($"rg: {level}|{target}|{file}:{line}: {message}");
    }
}
