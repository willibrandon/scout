using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Scout;

internal static class DifferentialRunner
{
    private const string PinnedRipgrepPath = "/Users/brandon/src/ripgrep/target/debug/rg";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object CurrentDirectoryLock = new();

    public static void AssertMatchesPinned(DifferentialCase testCase)
    {
        AssertMatchesPinned(testCase, workingDirectory: null);
    }

    public static void AssertMatchesPinned(DifferentialCase testCase, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        DifferentialRunResult scout = RunScout(testCase.Arguments, workingDirectory);
        DifferentialRunResult pinned = RunPinnedRipgrep(testCase.Arguments, workingDirectory);

        Assert.Equal(pinned.ExitCode, scout.ExitCode);
        AssertEqualBytes(
            testCase.Arguments,
            DifferentialOutputNormalizer.NormalizeStdout(pinned.Output, testCase.ComparisonMode),
            DifferentialOutputNormalizer.NormalizeStdout(scout.Output, testCase.ComparisonMode));
        Assert.Equal(
            DifferentialOutputNormalizer.NormalizeStderr(pinned.Error, testCase.ComparisonMode),
            DifferentialOutputNormalizer.NormalizeStderr(scout.Error, testCase.ComparisonMode));
    }

    private static DifferentialRunResult RunScout(string[] arguments, string? workingDirectory)
    {
        if (workingDirectory is null)
        {
            return RunScoutInCurrentDirectory(arguments);
        }

        lock (CurrentDirectoryLock)
        {
            string previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workingDirectory);
                return RunScoutInCurrentDirectory(arguments);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
    }

    private static DifferentialRunResult RunScoutInCurrentDirectory(string[] arguments)
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
        return new DifferentialRunResult(exitCode, output.ToArray(), Utf8.GetString(error.ToArray()));
    }

    private static DifferentialRunResult RunPinnedRipgrep(string[] arguments, string? workingDirectory)
    {
        ProcessStartInfo startInfo = new(PinnedRipgrepPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

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

        return new DifferentialRunResult(process.ExitCode, output.ToArray(), error);
    }

    private static void AssertEqualBytes(string[] arguments, byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        string joinedArguments = string.Join(" ", arguments);
        string message = "arguments: " + joinedArguments + "\nexpected:\n" + Utf8.GetString(expected) + "\nactual:\n" + Utf8.GetString(actual);
        Assert.Fail(message);
    }
}
