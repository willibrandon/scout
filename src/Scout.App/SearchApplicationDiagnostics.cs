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
            ? OsErrorMessages.NoSuchFileOrDirectory
            : $"IO error for operation on {path}: {OsErrorMessages.NoSuchFileOrDirectory}";
        return new ScoutError(message).WithContext($"rg: {path}");
    }
}
