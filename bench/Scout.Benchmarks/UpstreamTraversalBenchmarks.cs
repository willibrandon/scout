using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Scout.IO.Ignore;

namespace Scout;

/// <summary>
/// Measures entry-based ignore discovery and traversal over sibling search roots.
/// </summary>
[MemoryDiagnoser]
public class UpstreamTraversalBenchmarks
{
    private string root = string.Empty;
    private WalkBuilder builder = new();

    /// <summary>
    /// Gets or sets the number of directories in each search root.
    /// </summary>
    [Params(64, 512)]
    public int Directories { get; set; }

    /// <summary>
    /// Creates real fixtures and compares traversal output with the release executable.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "scout-traversal-bench-" + Guid.NewGuid().ToString("N"));
        string[] paths = [Path.Combine(root, "a"), Path.Combine(root, "b")];
        foreach (string path in paths)
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, ".rgignore"), "**/*.skip\n");
            for (int index = 0; index < Directories; index++)
            {
                string directory = Path.Combine(path, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "keep"), "needle");
                File.WriteAllText(Path.Combine(directory, "ignored.skip"), "needle");
            }
        }

        builder = WalkBuilder.FromPaths(paths).GitGlobal(false).Threads(4);
        if (Serial() != Directories * 2 || Parallel() != Directories * 2)
        {
            throw new InvalidOperationException("Traversal output changed.");
        }

        string oracle = Environment.GetEnvironmentVariable("SCOUT_TEST_RIPGREP_PATH")
            ?? throw new InvalidOperationException("Set SCOUT_TEST_RIPGREP_PATH to the release executable.");
        var start = new ProcessStartInfo(oracle) { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("--no-config");
        start.ArgumentList.Add("--no-ignore-global");
        start.ArgumentList.Add("--files");
        foreach (string path in paths)
        {
            start.ArgumentList.Add(path);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Reference process did not start.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string[] actual = builder.Build().Where(entry => entry.IsFile).Select(entry => entry.FullPath).Order(StringComparer.Ordinal).ToArray();
        string[] expected = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
        if (process.ExitCode != 0 || !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Reference traversal output differs.");
        }
    }

    /// <summary>
    /// Counts files through serial traversal.
    /// </summary>
    /// <returns>The number of files.</returns>
    [Benchmark]
    public int Serial() => builder.Build().Count(entry => entry.IsFile);

    /// <summary>
    /// Counts files through four parallel visitors.
    /// </summary>
    /// <returns>The number of files.</returns>
    [Benchmark]
    public int Parallel()
    {
        int count = 0;
        builder.BuildParallel().Run(() => entry =>
        {
            if (entry.IsFile)
            {
                Interlocked.Increment(ref count);
            }

            return WalkState.Continue;
        });
        return count;
    }

    /// <summary>
    /// Removes the temporary fixture after measurement.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(root, recursive: true);
}
