using System.Text;

namespace Scout;

/// <summary>
/// Verifies context separators at the boundary between standard-search output and statistics.
/// </summary>
public sealed class StandardSearchStatsSeparatorTests
{
    /// <summary>
    /// Verifies the stats boundary follows ripgrep's effective serial or parallel output mode.
    /// </summary>
    /// <param name="contextOption">The context option to exercise.</param>
    /// <param name="scenario">The search subject and execution-mode scenario.</param>
    /// <param name="expectsSeparator">Whether parallel buffering requires a separator before stats.</param>
    [Theory]
    [InlineData("-A1", "one-file-j2", false)]
    [InlineData("-A1", "files-j1", false)]
    [InlineData("-A1", "directory-j1", false)]
    [InlineData("-A1", "files-sort", false)]
    [InlineData("-A1", "directory-sort", false)]
    [InlineData("-A1", "files-j2", true)]
    [InlineData("-A1", "directory-j2", true)]
    [InlineData("-B1", "one-file-j2", false)]
    [InlineData("-B1", "files-j1", false)]
    [InlineData("-B1", "directory-j1", false)]
    [InlineData("-B1", "files-sort", false)]
    [InlineData("-B1", "directory-sort", false)]
    [InlineData("-B1", "files-j2", true)]
    [InlineData("-B1", "directory-j2", true)]
    [InlineData("-C1", "one-file-j2", false)]
    [InlineData("-C1", "files-j1", false)]
    [InlineData("-C1", "directory-j1", false)]
    [InlineData("-C1", "files-sort", false)]
    [InlineData("-C1", "directory-sort", false)]
    [InlineData("-C1", "files-j2", true)]
    [InlineData("-C1", "directory-j2", true)]
    public void ContextStatsBoundaryFollowsEffectiveOutputMode(
        string contextOption,
        string scenario,
        bool expectsSeparator)
    {
        ArgumentNullException.ThrowIfNull(contextOption);
        ArgumentNullException.ThrowIfNull(scenario);
        string root = CreateSearchDirectory();
        try
        {
            string firstPath = Path.Combine(root, "first.txt");
            string secondPath = Path.Combine(root, "second.txt");
            string[] arguments = CreateArguments(
                contextOption,
                scenario,
                root,
                firstPath,
                secondPath);

            (int exitCode, string output, string error) = RunScout(arguments);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error);
            string body = GetSearchBody(output);
            Assert.Equal(expectsSeparator, body.EndsWith("--\n", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a configured context separator is used at a parallel stats buffer boundary.
    /// </summary>
    [Fact]
    public void ParallelStatsBoundaryUsesConfiguredContextSeparator()
    {
        string root = CreateSearchDirectory();
        try
        {
            string firstPath = Path.Combine(root, "first.txt");
            string secondPath = Path.Combine(root, "second.txt");

            (int exitCode, string output, string error) = RunScout(
                "--no-config",
                "--stats",
                "--threads",
                "2",
                "--context",
                "1",
                "--context-separator=boundary",
                "needle",
                firstPath,
                secondPath);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error);
            Assert.EndsWith("boundary\n", GetSearchBody(output), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies disabling context separators also disables the parallel stats boundary separator.
    /// </summary>
    [Fact]
    public void DisabledContextSeparatorIsNotWrittenBeforeStats()
    {
        string root = CreateSearchDirectory();
        try
        {
            string firstPath = Path.Combine(root, "first.txt");
            string secondPath = Path.Combine(root, "second.txt");

            (int exitCode, string output, string error) = RunScout(
                "--no-config",
                "--stats",
                "--threads",
                "2",
                "--context",
                "1",
                "--no-context-separator",
                "needle",
                firstPath,
                secondPath);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error);
            Assert.DoesNotContain("--\n", GetSearchBody(output), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a parallel search with no output does not add a context separator before statistics.
    /// </summary>
    [Fact]
    public void ParallelNoMatchDoesNotWriteContextSeparatorBeforeStats()
    {
        string root = CreateSearchDirectory();
        try
        {
            string firstPath = Path.Combine(root, "first.txt");
            string secondPath = Path.Combine(root, "second.txt");

            (int exitCode, string output, string error) = RunScout(
                "--no-config",
                "--stats",
                "--threads",
                "2",
                "--context",
                "1",
                "absent",
                firstPath,
                secondPath);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, error);
            Assert.StartsWith("\n0 matches\n", output, StringComparison.Ordinal);
            Assert.DoesNotContain("--\n", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] CreateArguments(
        string contextOption,
        string scenario,
        string root,
        string firstPath,
        string secondPath)
    {
        string[] subjects = scenario.StartsWith("one-file", StringComparison.Ordinal)
            ? [firstPath]
            : scenario.StartsWith("directory", StringComparison.Ordinal)
                ? [root]
                : [firstPath, secondPath];
        string[] execution = scenario.EndsWith("j1", StringComparison.Ordinal)
            ? ["--threads", "1"]
            : scenario.EndsWith("j2", StringComparison.Ordinal)
                ? ["--threads", "2"]
                : ["--sort=path"];
        return
        [
            "--no-config",
            "--stats",
            .. execution,
            contextOption,
            "needle",
            .. subjects,
        ];
    }

    private static string CreateSearchDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "scout-stats-separator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "first.txt"), "before\nneedle\nafter\n");
        File.WriteAllText(Path.Combine(root, "second.txt"), "before\nneedle\nafter\n");
        return root;
    }

    private static string GetSearchBody(string output)
    {
        int statsIndex = output.IndexOf(" matches\n", StringComparison.Ordinal);
        Assert.True(statsIndex > 0, output);
        int statsLineStart = output.LastIndexOf('\n', statsIndex - 1) + 1;
        Assert.True(statsLineStart > 0, output);
        return output[..(statsLineStart - 1)];
    }

    private static (int ExitCode, string Output, string Error) RunScout(params string[] arguments)
    {
        using MemoryStream standardInput = new();
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        var osArguments = new OsString[arguments.Length + 1];
        osArguments[0] = OsString.FromText("scout");
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index + 1] = OsString.FromText(arguments[index]);
        }

        int exitCode = ScoutApplication.Run(
            osArguments,
            outputWriter,
            errorWriter,
            standardInput);
        return (
            exitCode,
            Encoding.UTF8.GetString(output.ToArray()),
            Encoding.UTF8.GetString(error.ToArray()));
    }
}
