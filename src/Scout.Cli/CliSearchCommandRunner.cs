using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace Scout;

internal static class CliSearchCommandRunner
{
    internal static bool TryRun(
        string path,
        string program,
        string[] arguments,
        bool pipeFileToStandardInput,
        bool fallbackOnStartError,
        out byte[] bytes,
        out ScoutError? error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(arguments);

        bytes = [];
        error = null;
        string commandDisplay = FormatCommandDisplay(program, arguments);
        using var process = new Process();
        process.StartInfo.FileName = program;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardInput = pipeFileToStandardInput;
        process.StartInfo.UseShellExecute = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            process.StartInfo.ArgumentList.Add(arguments[index]);
        }

        try
        {
            if (!process.Start())
            {
                if (fallbackOnStartError)
                {
                    return false;
                }

                error = new ScoutError($"preprocessor command could not start: '{commandDisplay}': process did not start");
                return false;
            }
        }
        catch (Win32Exception exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{commandDisplay}': {OsErrorMessages.FormatWin32Exception(exception)}");
            return false;
        }
        catch (InvalidOperationException exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{commandDisplay}': {exception.Message}");
            return false;
        }

        byte[] standardOutput = [];
        string standardError = string.Empty;
        ExceptionDispatchInfo? readerException = null;
        object readerExceptionLock = new();
        Thread standardOutputReader = StartReaderThread(() => standardOutput = ReadAllBytes(process.StandardOutput.BaseStream), CaptureReaderException);
        Thread standardErrorReader = StartReaderThread(() => standardError = process.StandardError.ReadToEnd(), CaptureReaderException);
        if (pipeFileToStandardInput)
        {
            CopyFileToProcessStandardInput(path, process);
        }

        process.WaitForExit();
        standardOutputReader.Join();
        standardErrorReader.Join();
        readerException?.Throw();
        bytes = standardOutput;
        if (process.ExitCode == 0)
        {
            return true;
        }

        string message = pipeFileToStandardInput
            ? $"preprocessor command failed: '{commandDisplay}': {FormatCommandStderr(standardError)}"
            : $"{program} command failed: {FormatCommandStderr(standardError)}";
        error = new ScoutError(message);
        bytes = [];
        return false;

        void CaptureReaderException(Exception exception)
        {
            lock (readerExceptionLock)
            {
                readerException ??= ExceptionDispatchInfo.Capture(exception);
            }
        }
    }

    private static void CopyFileToProcessStandardInput(string path, Process process)
    {
        using FileStream input = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                try
                {
                    process.StandardInput.BaseStream.Write(buffer, 0, read);
                }
                catch (IOException)
                {
                    break;
                }
            }
        }
        finally
        {
            CloseProcessStandardInput(process);
        }
    }

    private static void CloseProcessStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static Thread StartReaderThread(ThreadStart read, Action<Exception> captureException)
    {
        Thread thread = new(() =>
        {
            try
            {
                read();
            }
            catch (IOException exception)
            {
                captureException(exception);
            }
            catch (ObjectDisposedException exception)
            {
                captureException(exception);
            }
            catch (InvalidOperationException exception)
            {
                captureException(exception);
            }
            catch (NotSupportedException exception)
            {
                captureException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "scout-command-reader",
        };
        thread.Start();
        return thread;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string FormatCommandDisplay(string program, string[] arguments)
    {
        var builder = new StringBuilder();
        AppendQuotedCommandPart(builder, program);
        for (int index = 0; index < arguments.Length; index++)
        {
            builder.Append(' ');
            AppendQuotedCommandPart(builder, arguments[index]);
        }

        return builder.ToString();
    }

    private static void AppendQuotedCommandPart(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');
    }

    private static string FormatCommandStderr(string stderr)
    {
        string message = stderr.Trim();
        if (message.Length == 0)
        {
            return "<stderr is empty>";
        }

        string divider = new('-', 79);
        return "\n" + divider + "\n" + message + "\n" + divider;
    }
}
