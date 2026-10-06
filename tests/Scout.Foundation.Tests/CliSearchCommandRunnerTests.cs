
namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible external command execution for CLI preprocessing.
/// </summary>
[TestClass]
public sealed class CliSearchCommandRunnerTests
{
    /// <summary>
    /// Verifies decompression startup failures can fall back to raw file reading.
    /// </summary>
    [TestMethod]
    public void TryRunMissingCommandCanFallbackWithoutError()
    {
        string program = Path.Join(Path.GetTempPath(), "scout-missing-command-" + Guid.NewGuid().ToString("N"));

        bool ran = CliSearchCommandRunner.TryRun(
            path: program,
            program,
            arguments: [],
            pipeFileToStandardInput: false,
            fallbackOnStartError: true,
            out byte[] bytes,
            out ScoutError? error);

        Assert.IsFalse(ran);
        Assert.IsEmpty(bytes);
        Assert.IsNull(error);
    }

    /// <summary>
    /// Verifies preprocessor startup failures are reported as user-facing errors.
    /// </summary>
    [TestMethod]
    public void TryRunMissingPreprocessorReportsError()
    {
        string program = Path.Join(Path.GetTempPath(), "scout-missing-command-" + Guid.NewGuid().ToString("N"));

        bool ran = CliSearchCommandRunner.TryRun(
            path: program,
            program,
            arguments: [],
            pipeFileToStandardInput: false,
            fallbackOnStartError: false,
            out byte[] bytes,
            out ScoutError? error);

        Assert.IsFalse(ran);
        Assert.IsEmpty(bytes);
        Assert.IsNotNull(error);
        string escapedProgram = program.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        Assert.StartsWith($"preprocessor command could not start: '\"{escapedProgram}\"': ", error!.Message, StringComparison.Ordinal);
    }
}
