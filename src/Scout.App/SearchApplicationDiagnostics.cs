namespace Scout;

internal static class SearchApplicationDiagnostics
{
    internal static void ReportError(CliLowArgs lowArgs, DiagnosticMessenger diagnostics, ScoutError error)
    {
        if (lowArgs.Messages)
        {
            diagnostics.ErrorMessage(error);
            return;
        }

        diagnostics.MarkErrored();
    }

    internal static ScoutError MissingPath(string path, bool simple = false)
    {
        string message = simple
            ? "No such file or directory (os error 2)"
            : $"IO error for operation on {path}: No such file or directory (os error 2)";
        return new ScoutError(message).WithContext($"rg: {path}");
    }
}
