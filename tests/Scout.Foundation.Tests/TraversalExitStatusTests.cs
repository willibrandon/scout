using System.Text;

namespace Scout;

/// <summary>
/// Verifies CLI exit status after real traversal failures with otherwise searchable files.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class TraversalExitStatusTests
{
    /// <summary>
    /// Supplies search modes, traversal modes, and diagnostic policies.
    /// </summary>
    /// <returns>The CLI scenarios to exercise.</returns>
    public static IEnumerable<(string[] Mode, string Traversal, bool Messages, bool Quiet)> FailureCases()
    {
        string[][] modes = [[], ["--files"], ["--json"], ["--stats"]];
        foreach (string[] mode in modes)
        {
            foreach (string traversal in new[] { "-j1", "-j4", "--sort=path" })
            {
                foreach (bool messages in new[] { false, true })
                {
                    foreach (bool quiet in new[] { false, true })
                    {
                        yield return (mode, traversal, messages, quiet);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verifies a followed link that cannot be resolved affects status even when another file matches.
    /// </summary>
    /// <param name="mode">The search or listing mode.</param>
    /// <param name="traversal">The traversal worker or sorting option.</param>
    /// <param name="messages">Whether error messages are enabled.</param>
    /// <param name="quiet">Whether a match overrides errors.</param>
    [TestMethod]
    [DynamicData(nameof(FailureCases))]
    public void TraversalFailureAffectsExitStatus(string[] mode, string traversal, bool messages, bool quiet)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(traversal);
        string root = Directory.CreateTempSubdirectory("scout-exit-").FullName;
        try
        {
            string broken = Path.Join(root, "broken");
            File.CreateSymbolicLink(broken, broken);
            File.WriteAllText(Path.Join(root, "matched.txt"), "needle\n");
            List<string> arguments = ["--no-config", "--no-ignore", "--follow", traversal];
            arguments.AddRange(mode);
            arguments.Add(messages ? "--messages" : "--no-messages");
            if (quiet)
            {
                arguments.Add("--quiet");
            }

            if (!mode.Contains("--files"))
            {
                arguments.Add("needle");
            }

            arguments.Add(root);
            (int exitCode, string output, string error) = Run(arguments);
            Assert.AreEqual(quiet ? ExitCode.Success : ExitCode.Error, exitCode);
            if (quiet && !mode.Contains("--stats") && !mode.Contains("--json"))
            {
                Assert.IsEmpty(output);
            }
            else if (!quiet)
            {
                Assert.Contains("matched.txt", output, StringComparison.Ordinal);
                if (messages)
                {
                    Assert.Contains("broken", error, StringComparison.Ordinal);
                }
            }

            if (!messages)
            {
                Assert.IsEmpty(error);
            }

            // A failed traversal must not affect a later invocation in the same process.
            (int nextExitCode, _, string nextError) = Run(["--no-config", "needle", Path.Join(root, "matched.txt")]);
            Assert.AreEqual(ExitCode.Success, nextExitCode);
            Assert.IsEmpty(nextError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies quiet mode still reports failure when there is no match to override it.
    /// </summary>
    /// <param name="mode">The search mode.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("--json")]
    [DataRow("--stats")]
    public void QuietWithoutMatchPreservesTraversalFailure(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        string root = Directory.CreateTempSubdirectory("scout-exit-").FullName;
        try
        {
            string broken = Path.Join(root, "broken");
            File.CreateSymbolicLink(broken, broken);
            File.WriteAllText(Path.Join(root, "miss.txt"), "haystack\n");
            List<string> arguments = ["--no-config", "--no-ignore", "--follow", "--sort=path", "--quiet", "--no-messages"];
            if (mode.Length > 0)
            {
                arguments.Add(mode);
            }

            arguments.AddRange(["needle", root]);
            (int exitCode, _, string error) = Run(arguments);
            Assert.AreEqual(ExitCode.Error, exitCode);
            Assert.IsEmpty(error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Output, string Error) Run(IReadOnlyList<string> arguments)
    {
        using MemoryStream input = new();
        using MemoryStream output = new();
        using MemoryStream error = new();
        OsString[] osArguments = [OsString.FromText("scout"), .. arguments.Select(OsString.FromText)];
        int exitCode = ScoutApplication.Run(osArguments, new RawByteWriter(output), new RawByteWriter(error), input, configPath: null);
        return (exitCode, Encoding.UTF8.GetString(output.ToArray()), Encoding.UTF8.GetString(error.ToArray()));
    }
}
