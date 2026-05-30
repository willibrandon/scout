using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Scout;

internal static class DifferentialRunner
{
    private const string PinnedRipgrepPath = "/Users/brandon/src/ripgrep/target/debug/rg";
    private const int PinnedRipgrepTimeoutMilliseconds = 10_000;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object CurrentDirectoryLock = new();

    public static void AssertMatchesPinned(DifferentialCase testCase)
    {
        AssertMatchesPinned(testCase, workingDirectory: null);
    }

    public static void AssertMatchesPinned(DifferentialCase testCase, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        DifferentialRunResult scout = RunScout(testCase.Arguments, testCase.StandardInput, workingDirectory);
        DifferentialRunResult pinned = RunPinnedRipgrep(testCase.Arguments, testCase.StandardInput, workingDirectory);

        if (pinned.ExitCode != scout.ExitCode)
        {
            Assert.Fail(BuildFailureMessage(testCase.Arguments, pinned, scout));
        }

        AssertEqualBytes(
            testCase.Arguments,
            DifferentialOutputNormalizer.NormalizeStdout(pinned.Output, testCase.ComparisonMode),
            DifferentialOutputNormalizer.NormalizeStdout(scout.Output, testCase.ComparisonMode));
        Assert.Equal(
            DifferentialOutputNormalizer.NormalizeStderr(pinned.Error, testCase.ComparisonMode),
            DifferentialOutputNormalizer.NormalizeStderr(scout.Error, testCase.ComparisonMode));
    }

    private static DifferentialRunResult RunScout(string[] arguments, byte[]? standardInput, string? workingDirectory)
    {
        if (workingDirectory is null)
        {
            return RunScoutInCurrentDirectory(arguments, standardInput);
        }

        lock (CurrentDirectoryLock)
        {
            string previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(workingDirectory);
                return RunScoutInCurrentDirectory(arguments, standardInput);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
    }

    private static DifferentialRunResult RunScoutInCurrentDirectory(string[] arguments, byte[]? standardInput)
    {
        using MemoryStream input = new(standardInput ?? []);
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

        int exitCode = ScoutApplication.Run(osArguments, outputWriter, errorWriter, input);
        outputWriter.Flush();
        errorWriter.Flush();
        return new DifferentialRunResult(exitCode, output.ToArray(), Utf8.GetString(error.ToArray()));
    }

    private static DifferentialRunResult RunPinnedRipgrep(string[] arguments, byte[]? standardInput, string? workingDirectory)
    {
        ProcessStartInfo startInfo = new(PinnedRipgrepPath)
        {
            RedirectStandardError = true,
            RedirectStandardInput = true,
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
        if (standardInput is not null)
        {
            process.StandardInput.BaseStream.Write(standardInput);
        }

        process.StandardInput.Close();

        using MemoryStream output = new();
        string error = string.Empty;
        Exception? outputException = null;
        Exception? errorException = null;
        var outputThread = new Thread(() =>
        {
            try
            {
                process.StandardOutput.BaseStream.CopyTo(output);
            }
            catch (IOException exception)
            {
                outputException = exception;
            }
            catch (ObjectDisposedException exception)
            {
                outputException = exception;
            }
        });
        var errorThread = new Thread(() =>
        {
            try
            {
                error = process.StandardError.ReadToEnd();
            }
            catch (IOException exception)
            {
                errorException = exception;
            }
            catch (ObjectDisposedException exception)
            {
                errorException = exception;
            }
        });
        outputThread.Start();
        errorThread.Start();
        bool timedOut = false;
        if (!process.WaitForExit(PinnedRipgrepTimeoutMilliseconds))
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        outputThread.Join();
        errorThread.Join();
        if (timedOut)
        {
            Assert.Fail("Pinned ripgrep timed out for arguments: " + string.Join(" ", arguments));
        }

        if (outputException is not null)
        {
            throw outputException;
        }

        if (errorException is not null)
        {
            throw errorException;
        }

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

    private static string BuildFailureMessage(string[] arguments, DifferentialRunResult expected, DifferentialRunResult actual)
    {
        string joinedArguments = string.Join(" ", arguments);
        return "arguments: " + joinedArguments +
            "\nexpected exit code: " + expected.ExitCode +
            "\nactual exit code: " + actual.ExitCode +
            "\nexpected stdout:\n" + Utf8.GetString(expected.Output) +
            "\nactual stdout:\n" + Utf8.GetString(actual.Output) +
            "\nexpected stderr:\n" + expected.Error +
            "\nactual stderr:\n" + actual.Error;
    }
}
