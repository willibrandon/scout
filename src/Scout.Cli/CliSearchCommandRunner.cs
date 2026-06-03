using System.ComponentModel;
using System.Diagnostics;
using System.Text;

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
        using var standardOutputReader = CliBackgroundWorkItem.Queue(() => standardOutput = ReadAllBytes(process.StandardOutput.BaseStream));
        using var standardErrorReader = CliBackgroundWorkItem.Queue(() => standardError = process.StandardError.ReadToEnd());
        if (pipeFileToStandardInput)
        {
            CopyFileToProcessStandardInput(path, process);
        }

        process.WaitForExit();
        standardOutputReader.Join();
        standardErrorReader.Join();
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
