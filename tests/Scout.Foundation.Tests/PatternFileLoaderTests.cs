
namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible pattern-file loading.
/// </summary>
public sealed class PatternFileLoaderTests
{
    /// <summary>
    /// Verifies standard-input pattern files split lines and trim CRLF terminators.
    /// </summary>
    [Fact]
    public void TryLoadReadsPatternsFromStandardInput()
    {
        using MemoryStream input = new("alpha\r\n\r\nbeta\n"u8.ToArray());
        using MemoryStream error = new();
        var diagnostics = new DiagnosticMessenger(new RawByteWriter(error), new DiagnosticState());
        var patterns = new List<byte[]>();

        bool loaded = PatternFileLoader.TryLoad(OsString.FromText("-"), patterns, input, diagnostics);

        Assert.True(loaded);
        Assert.Equal(3, patterns.Count);
        Assert.Equal("alpha"u8.ToArray(), patterns[0]);
        Assert.Empty(patterns[1]);
        Assert.Equal("beta"u8.ToArray(), patterns[2]);
        Assert.Empty(error.ToArray());
    }

    /// <summary>
    /// Verifies invalid UTF-8 pattern bytes report the exact byte offset and escaped line.
    /// </summary>
    [Fact]
    public void TryLoadReportsInvalidUtf8PatternBytes()
    {
        using MemoryStream input = new([(byte)'a', 0xFF, (byte)'\n']);
        using MemoryStream error = new();
        var diagnostics = new DiagnosticMessenger(new RawByteWriter(error), new DiagnosticState());
        var patterns = new List<byte[]>();

        bool loaded = PatternFileLoader.TryLoad(OsString.FromText("-"), patterns, input, diagnostics);

        Assert.False(loaded);
        Assert.Empty(patterns);
        Assert.Equal(
            "scout: -:1: found invalid UTF-8 in pattern at byte offset 1: a\\xFF (disable Unicode mode and use hex escape sequences to match arbitrary bytes in a pattern, e.g., '(?-u)\\xFF')\n"u8.ToArray(),
            error.ToArray());
    }

    /// <summary>
    /// Verifies non-text pattern-file path arguments match ripgrep's CLI-argument diagnostic.
    /// </summary>
    [Fact]
    public void TryLoadRejectsInvalidUtf8PathArgument()
    {
        using MemoryStream input = new();
        using MemoryStream error = new();
        var diagnostics = new DiagnosticMessenger(new RawByteWriter(error), new DiagnosticState());
        var patterns = new List<byte[]>();

        bool loaded = PatternFileLoader.TryLoad(OsString.FromUnixBytes([0xFF]), patterns, input, diagnostics);

        Assert.False(loaded);
        Assert.Empty(patterns);
        Assert.Equal("scout: invalid CLI arguments\n"u8.ToArray(), error.ToArray());
    }
}
