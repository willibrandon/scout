using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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

                error = new ScoutError($"preprocessor command could not start: '{program}': process did not start");
                return false;
            }
        }
        catch (Win32Exception exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{program}': {exception.Message}");
            return false;
        }
        catch (InvalidOperationException exception)
        {
            if (fallbackOnStartError)
            {
                return false;
            }

            error = new ScoutError($"preprocessor command could not start: '{program}': {exception.Message}");
            return false;
        }

        Task<byte[]> standardOutput = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (pipeFileToStandardInput)
        {
            CopyFileToProcessStandardInput(path, process);
        }

        process.WaitForExit();
        bytes = standardOutput.GetAwaiter().GetResult();
        string stderr = standardError.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
        {
            return true;
        }

        string message = pipeFileToStandardInput
            ? $"preprocessor command failed: '{program}': {stderr.TrimEnd()}"
            : $"{program} command failed: {stderr.TrimEnd()}";
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

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
