using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies byte-for-byte search parity against the pinned ripgrep binary.
/// </summary>
public sealed class DifferentialSearchTests
{
    private const string PinnedRipgrepPath = "/Users/brandon/src/ripgrep/target/debug/rg";

    /// <summary>
    /// Verifies a baseline flag matrix against the pinned ripgrep binary.
    /// </summary>
    [Fact]
    public void BaselineSearchMatrixMatchesPinnedRipgrep()
    {
        string root = CreateTempDirectory();
        try
        {
            string first = Path.Combine(root, "first.txt");
            string second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "needle\nmiss\nneedle again\n");
            File.WriteAllText(second, "alpha\nneedle second\n");

            DifferentialCase[] cases =
            [
                DifferentialCase.Exact("needle", first),
                DifferentialCase.Exact("-nH", "needle", first),
                DifferentialCase.Exact("-nA1", "needle", first),
                DifferentialCase.Exact("-v", "needle", first),
                DifferentialCase.Exact("-c", "needle", first),
                DifferentialCase.Exact("--count-matches", "needle", first),
                DifferentialCase.Exact("-o", "needle", first),
                DifferentialCase.Exact("--sort=path", "-H", "needle", root),
                DifferentialCase.Exact("-ne", "needle", first),
                DifferentialCase.Exact("-m1", "needle", first),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLines, "-l", "needle", first, second),
                DifferentialCase.Normalized(DifferentialComparisonMode.MaskElapsed, "--stats", "needle", first),
                DifferentialCase.Normalized(DifferentialComparisonMode.SortLinesAndMaskElapsed, "--json", "needle", root),
            ];

            for (int index = 0; index < cases.Length; index++)
            {
                DifferentialCase testCase = cases[index];
                string[] arguments = testCase.Arguments;
                (int scoutExitCode, byte[] scoutOutput, string scoutError) = RunScout(arguments);
                (int pinnedExitCode, byte[] pinnedOutput, string pinnedError) = RunPinnedRipgrep(arguments);

                Assert.Equal(pinnedExitCode, scoutExitCode);
                AssertEqualBytes(
                    arguments,
                    DifferentialOutputNormalizer.NormalizeStdout(pinnedOutput, testCase.ComparisonMode),
                    DifferentialOutputNormalizer.NormalizeStdout(scoutOutput, testCase.ComparisonMode));
                Assert.Equal(
                    DifferentialOutputNormalizer.NormalizeStderr(pinnedError, testCase.ComparisonMode),
                    DifferentialOutputNormalizer.NormalizeStderr(scoutError, testCase.ComparisonMode));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, byte[] Output, string Error) RunScout(params string[] arguments)
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        var osArguments = new OsString[arguments.Length + 1];
        osArguments[0] = OsString.FromUnixBytes("scout"u8);
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index + 1] = OsString.FromText(arguments[index]);
        }

        int exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter);
        outputWriter.Flush();
        errorWriter.Flush();
        return (exitCode, output.ToArray(), Utf8(error.ToArray()));
    }

    private static (int ExitCode, byte[] Output, string Error) RunPinnedRipgrep(params string[] arguments)
    {
        ProcessStartInfo startInfo = new(PinnedRipgrepPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Remove("RIPGREP_CONFIG_PATH");
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        using MemoryStream output = new();
        process.StandardOutput.BaseStream.CopyTo(output);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output.ToArray(), error);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scout-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertEqualBytes(string[] arguments, byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        string joinedArguments = string.Join(" ", arguments);
        string message = "arguments: " + joinedArguments + "\nexpected:\n" + Utf8(expected) + "\nactual:\n" + Utf8(actual);
        Assert.Fail(message);
    }

    private static string Utf8(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }
}
