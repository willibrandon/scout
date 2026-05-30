namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible decompression command matching.
/// </summary>
public sealed class CliDecompressionMatcherTests
{
    /// <summary>
    /// Verifies the default decompression command table matches the pinned upstream table.
    /// </summary>
    [Fact]
    public void DefaultCommandsMatchPinnedRipgrepTable()
    {
        (string Glob, string Program, string[] Arguments)[] expected =
        [
            ("*.gz", "gzip", ["-d", "-c"]),
            ("*.tgz", "gzip", ["-d", "-c"]),
            ("*.bz2", "bzip2", ["-d", "-c"]),
            ("*.tbz2", "bzip2", ["-d", "-c"]),
            ("*.xz", "xz", ["-d", "-c"]),
            ("*.txz", "xz", ["-d", "-c"]),
            ("*.lz4", "lz4", ["-d", "-c"]),
            ("*.lzma", "xz", ["--format=lzma", "-d", "-c"]),
            ("*.br", "brotli", ["-d", "-c"]),
            ("*.zst", "zstd", ["-q", "-d", "-c"]),
            ("*.zstd", "zstd", ["-q", "-d", "-c"]),
            ("*.Z", "uncompress", ["-c"]),
        ];

        Assert.Equal(expected.Length, CliDecompressionMatcher.DefaultCommands.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            CliDecompressionCommand command = CliDecompressionMatcher.DefaultCommands[index];

            Assert.Equal(expected[index].Glob, command.Glob);
            Assert.Equal(expected[index].Program, command.Program);
            Assert.Equal(expected[index].Arguments, command.Arguments);
        }
    }

    /// <summary>
    /// Verifies default command matching uses ripgrep's case-sensitive glob suffixes.
    /// </summary>
    [Theory]
    [InlineData("archive.gz", "gzip")]
    [InlineData("archive.tar.gz", "gzip")]
    [InlineData("archive.lzma", "xz")]
    [InlineData("archive.Z", "uncompress")]
    public void TryGetDefaultCommandMatchesKnownSuffixes(string path, string expectedProgram)
    {
        bool matched = CliDecompressionMatcher.TryGetDefaultCommand(path, out CliDecompressionCommand? command);

        Assert.True(matched);
        Assert.NotNull(command);
        Assert.Equal(expectedProgram, command.Program);
    }

    /// <summary>
    /// Verifies default command matching rejects unknown or differently cased suffixes.
    /// </summary>
    [Theory]
    [InlineData("archive.zip")]
    [InlineData("archive.GZ")]
    [InlineData("archive.z")]
    public void TryGetDefaultCommandRejectsUnknownSuffixes(string path)
    {
        bool matched = CliDecompressionMatcher.TryGetDefaultCommand(path, out CliDecompressionCommand? command);

        Assert.False(matched);
        Assert.Null(command);
    }

    /// <summary>
    /// Verifies command argument creation appends the searched path without mutating fixed arguments.
    /// </summary>
    [Fact]
    public void CreateArgumentsAppendsPath()
    {
        CliDecompressionCommand command = new("*.test", "program", "-d", "-c");

        string[] arguments = command.CreateArguments("input.test");
        arguments[0] = "--changed";

        Assert.Equal(["-d", "-c", "input.test"], command.CreateArguments("input.test"));
        Assert.Equal(["-d", "-c"], command.Arguments);
    }
}
