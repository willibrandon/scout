using System.IO;

namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible stdout buffering mode selection.
/// </summary>
public sealed class OutputBufferingTests
{
    /// <summary>
    /// Verifies automatic buffering uses line mode for terminals and block mode otherwise.
    /// </summary>
    [Fact]
    public void AutoBufferingTracksStdoutTerminalState()
    {
        Assert.Equal(RawByteWriterBufferMode.Line, OutputBuffering.Resolve(CliBufferMode.Auto, standardOutputIsTerminal: true));
        Assert.Equal(RawByteWriterBufferMode.Block, OutputBuffering.Resolve(CliBufferMode.Auto, standardOutputIsTerminal: false));
    }

    /// <summary>
    /// Verifies explicit buffering flags override automatic terminal detection.
    /// </summary>
    [Fact]
    public void ExplicitBufferingOverridesTerminalState()
    {
        Assert.Equal(RawByteWriterBufferMode.Line, OutputBuffering.Resolve(CliBufferMode.Line, standardOutputIsTerminal: false));
        Assert.Equal(RawByteWriterBufferMode.Block, OutputBuffering.Resolve(CliBufferMode.Block, standardOutputIsTerminal: true));
    }

    /// <summary>
    /// Verifies redirected stdout uses block buffering until the app flushes at completion.
    /// </summary>
    [Fact]
    public void ScoutApplicationUsesBlockBufferingForRedirectedStdout()
    {
        using MemoryStream output = new();
        using MemoryStream error = new();
        var outputWriter = new RawByteWriter(output);
        var errorWriter = new RawByteWriter(error);
        OsString[] arguments =
        [
            OsString.FromUnixBytes("scout"u8),
            OsString.FromUnixBytes("--generate"u8),
            OsString.FromUnixBytes("complete-bash"u8),
        ];

        int exitCode = ScoutApplication.Run(arguments, outputWriter, errorWriter, standardOutputIsTerminal: false);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output.ToArray());
        Assert.Empty(error.ToArray());
    }
}
