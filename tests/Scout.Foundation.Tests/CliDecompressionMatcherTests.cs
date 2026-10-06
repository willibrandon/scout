namespace Scout;

/// <summary>
/// Verifies ripgrep-compatible decompression command matching.
/// </summary>
[TestClass]
public sealed class CliDecompressionMatcherTests
{
    /// <summary>
    /// Verifies the default decompression command table matches the pinned upstream table.
    /// </summary>
    [TestMethod]
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

        Assert.HasCount(expected.Length, CliDecompressionMatcher.DefaultCommands);
        for (int index = 0; index < expected.Length; index++)
        {
            CliDecompressionCommand command = CliDecompressionMatcher.DefaultCommands[index];

            Assert.AreEqual(expected[index].Glob, command.Glob);
            Assert.AreEqual(expected[index].Program, command.Program);
            Assert.AreSequenceEqual(expected[index].Arguments, command.Arguments);
        }
    }

    /// <summary>
    /// Verifies default command matching uses ripgrep's case-sensitive glob suffixes.
    /// </summary>
    [TestMethod]
    [DataRow("archive.gz", "gzip")]
    [DataRow("archive.tar.gz", "gzip")]
    [DataRow("archive.lzma", "xz")]
    [DataRow("archive.Z", "uncompress")]
    public void TryGetDefaultCommandMatchesKnownSuffixes(string path, string expectedProgram)
    {
        bool matched = CliDecompressionMatcher.TryGetDefaultCommand(path, out CliDecompressionCommand? command);

        Assert.IsTrue(matched);
        Assert.IsNotNull(command);
        Assert.AreEqual(expectedProgram, command.Program);
    }

    /// <summary>
    /// Verifies default command matching rejects unknown or differently cased suffixes.
    /// </summary>
    [TestMethod]
    [DataRow("archive.zip")]
    [DataRow("archive.GZ")]
    [DataRow("archive.z")]
    public void TryGetDefaultCommandRejectsUnknownSuffixes(string path)
    {
        bool matched = CliDecompressionMatcher.TryGetDefaultCommand(path, out CliDecompressionCommand? command);

        Assert.IsFalse(matched);
        Assert.IsNull(command);
    }

    /// <summary>
    /// Verifies command argument creation appends the searched path without mutating fixed arguments.
    /// </summary>
    [TestMethod]
    public void CreateArgumentsAppendsPath()
    {
        CliDecompressionCommand command = new("*.test", "program", "-d", "-c");

        string[] arguments = command.CreateArguments("input.test");
        arguments[0] = "--changed";

        Assert.AreSequenceEqual<string>(["-d", "-c", "input.test"], command.CreateArguments("input.test"));
        Assert.AreSequenceEqual<string>(["-d", "-c"], command.Arguments);
    }

    /// <summary>
    /// Verifies non-Windows decompression binary resolution follows ripgrep's no-op behavior.
    /// </summary>
    [TestMethod]
    public void TryResolveBinaryDoesNotSearchPathOnNonWindows()
    {
        bool resolved = CliDecompressionMatcher.TryResolveBinary(
            "gzip",
            pathVariable: null,
            isWindows: false,
            pathSeparator: ':',
            _ => false,
            out string program);

        Assert.IsTrue(resolved);
        Assert.AreEqual("gzip", program);
    }

    /// <summary>
    /// Verifies Windows decompression binary resolution finds bare and suffixed executables in <c>PATH</c>.
    /// </summary>
    [TestMethod]
    public void TryResolveBinarySearchesWindowsPathWithExecutableSuffixes()
    {
        bool resolvedCom = CliDecompressionMatcher.TryResolveBinary(
            "gzip",
            "/missing;/tools",
            isWindows: true,
            pathSeparator: ';',
            path => path == Path.Join("/tools", "gzip.com"),
            out string comProgram);
        bool resolvedExe = CliDecompressionMatcher.TryResolveBinary(
            "xz",
            "/tools",
            isWindows: true,
            pathSeparator: ';',
            path => path == Path.Join("/tools", "xz.exe"),
            out string exeProgram);
        bool resolvedExact = CliDecompressionMatcher.TryResolveBinary(
            "brotli.exe",
            "/tools",
            isWindows: true,
            pathSeparator: ';',
            path => path == Path.Join("/tools", "brotli.exe"),
            out string exactProgram);

        Assert.IsTrue(resolvedCom);
        Assert.AreEqual(Path.Join("/tools", "gzip.com"), comProgram);
        Assert.IsTrue(resolvedExe);
        Assert.AreEqual(Path.Join("/tools", "xz.exe"), exeProgram);
        Assert.IsTrue(resolvedExact);
        Assert.AreEqual(Path.Join("/tools", "brotli.exe"), exactProgram);
    }

    /// <summary>
    /// Verifies Windows decompression binary resolution reports unresolved commands instead of falling back to the current directory.
    /// </summary>
    [TestMethod]
    public void TryResolveBinaryRejectsMissingWindowsPathExecutables()
    {
        bool resolved = CliDecompressionMatcher.TryResolveBinary(
            "gzip",
            "/missing",
            isWindows: true,
            pathSeparator: ';',
            _ => false,
            out string program);

        Assert.IsFalse(resolved);
        Assert.IsEmpty(program);
    }
}
