using System.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies standard-input behavior through real managed child-process pipes.
/// </summary>
public sealed class StandardInputProcessTests
{
    /// <summary>
    /// Verifies matching redirected input is searched after the parent closes the pipe writer.
    /// </summary>
    /// <param name="explicitStandardInput">Whether the command names standard input with <c>-</c>.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingRedirectedInputIsSearchedAtPipeEndOfFileAsync(
        bool explicitStandardInput)
    {
        byte[] input = "ScoutScout\r\n"u8.ToArray();
        string[] arguments = explicitStandardInput
            ? ["--no-config", "-n", "--stats", "Scout", "-"]
            : ["--no-config", "-n", "--stats", "Scout"];

        (int exitCode, string output, string error) = await RunHostAsync(
            input,
            arguments).ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.StartsWith("1:ScoutScout\r\n\n", output, StringComparison.Ordinal);
        Assert.Contains("2 matches\n", output, StringComparison.Ordinal);
        Assert.Contains("1 matched lines\n", output, StringComparison.Ordinal);
        Assert.Contains("12 bytes searched\n", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies nonmatching redirected input returns ripgrep's no-match exit code at pipe EOF.
    /// </summary>
    /// <param name="explicitStandardInput">Whether the command names standard input with <c>-</c>.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonmatchingRedirectedInputReturnsNoMatchAtPipeEndOfFileAsync(
        bool explicitStandardInput)
    {
        byte[] input = "NoMatch\r\n"u8.ToArray();
        string[] arguments = explicitStandardInput
            ? ["--no-config", "--stats", "Scout", "-"]
            : ["--no-config", "--stats", "Scout"];

        (int exitCode, string output, string error) = await RunHostAsync(
            input,
            arguments).ConfigureAwait(true);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, error);
        Assert.StartsWith("\n0 matches\n", output, StringComparison.Ordinal);
        Assert.Contains("0 matched lines\n", output, StringComparison.Ordinal);
        Assert.Contains("9 bytes searched\n", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunHostAsync(
        byte[] input,
        params string[] arguments)
    {
        string hostPath = Path.Combine(
            AppContext.BaseDirectory,
            "Scout.App.ProcessHost.dll");
        Assert.True(File.Exists(hostPath), hostPath);
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment.Remove("RIPGREP_CONFIG_PATH");
        startInfo.Environment.Remove("SCOUT_CONFIG_PATH");
        startInfo.ArgumentList.Add(hostPath);
        for (int index = 0; index < arguments.Length; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
        };
        Assert.True(process.Start());
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(false);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException("The standard-input process host did not exit within 30 seconds.");
        }

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        return (process.ExitCode, output, error);
    }
}
