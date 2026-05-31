using System;
using System.IO;

namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible external command execution for CLI preprocessing.
/// </summary>
public sealed class CliSearchCommandRunnerTests
{
    /// <summary>
    /// Verifies decompression startup failures can fall back to raw file reading.
    /// </summary>
    [Fact]
    public void TryRunMissingCommandCanFallbackWithoutError()
    {
        string program = Path.Combine(Path.GetTempPath(), "scout-missing-command-" + Guid.NewGuid().ToString("N"));

        bool ran = CliSearchCommandRunner.TryRun(
            path: program,
            program,
            arguments: [],
            pipeFileToStandardInput: false,
            fallbackOnStartError: true,
            out byte[] bytes,
            out ScoutError? error);

        Assert.False(ran);
        Assert.Empty(bytes);
        Assert.Null(error);
    }

    /// <summary>
    /// Verifies preprocessor startup failures are reported as user-facing errors.
    /// </summary>
    [Fact]
    public void TryRunMissingPreprocessorReportsError()
    {
        string program = Path.Combine(Path.GetTempPath(), "scout-missing-command-" + Guid.NewGuid().ToString("N"));

        bool ran = CliSearchCommandRunner.TryRun(
            path: program,
            program,
            arguments: [],
            pipeFileToStandardInput: false,
            fallbackOnStartError: false,
            out byte[] bytes,
            out ScoutError? error);

        Assert.False(ran);
        Assert.Empty(bytes);
        Assert.NotNull(error);
        string escapedProgram = program.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        Assert.StartsWith($"preprocessor command could not start: '\"{escapedProgram}\"': ", error!.Message, StringComparison.Ordinal);
    }
}
