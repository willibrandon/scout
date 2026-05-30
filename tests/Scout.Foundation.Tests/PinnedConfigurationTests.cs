using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scout;

/// <summary>
/// Verifies the repository-level pins required by the Scout design.
/// </summary>
public sealed class PinnedConfigurationTests
{
    private const string PinnedRipgrepCommit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119";
    private const string ReferenceRipgrepRoot = "/Users/brandon/src/ripgrep";
    private const string PinnedRipgrepBinaryPath = "/Users/brandon/src/ripgrep/target/debug/rg";
    private const string PinnedPcre2Commit = "56c87ccac13b01c3c1ecdf71e4fc2fedccea50a2";

    /// <summary>
    /// Verifies the SDK pin uses the exact .NET 10 SDK required by the design.
    /// </summary>
    [Fact]
    public void GlobalJsonPinsExactSdk()
    {
        string root = FindRepositoryRoot();
        string json = File.ReadAllText(Path.Combine(root, "global.json"));
        using var document = JsonDocument.Parse(json);

        JsonElement sdk = document.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.102", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
    }

    /// <summary>
    /// Verifies the solution uses the SDK's XML solution format.
    /// </summary>
    [Fact]
    public void RepositoryUsesSlnxSolution()
    {
        string root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "Scout.slnx")));
        Assert.False(File.Exists(Path.Combine(root, "Scout.sln")));
    }

    /// <summary>
    /// Verifies the local ripgrep reference checkout is at the design pin.
    /// </summary>
    [Fact]
    public void ReferenceRipgrepCheckoutMatchesPinnedCommit()
    {
        Assert.True(Directory.Exists(ReferenceRipgrepRoot), "Missing reference checkout: " + ReferenceRipgrepRoot);

        (int exitCode, string output, string error) = RunProcess("git", ["-C", ReferenceRipgrepRoot, "rev-parse", "HEAD"]);

        Assert.True(exitCode == 0, error);
        Assert.Equal(PinnedRipgrepCommit, output.Trim());
    }

    /// <summary>
    /// Verifies the pinned ripgrep binary exists and reports the pinned revision.
    /// </summary>
    [Fact]
    public void PinnedRipgrepBinaryMatchesPinnedRevision()
    {
        Assert.True(File.Exists(PinnedRipgrepBinaryPath), "Missing pinned ripgrep binary: " + PinnedRipgrepBinaryPath);

        (int exitCode, string output, string error) = RunProcess(PinnedRipgrepBinaryPath, ["--version"]);

        Assert.True(exitCode == 0, error);
        Assert.StartsWith("ripgrep 15.1.0 (rev 4857d6fa67)", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the centrally pinned package versions named by the design.
    /// </summary>
    [Fact]
    public void CentralPackageVersionsPinToolchainAndTests()
    {
        string root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));

        AssertPackageVersion(document, "Microsoft.DotNet.ILCompiler", "10.0.2");
        AssertPackageVersion(document, "Microsoft.CodeAnalysis.NetAnalyzers", "10.0.102");
        AssertPackageVersion(document, "BenchmarkDotNet", "0.15.8");
        AssertPackageVersion(document, "xunit.v3", "3.2.2");
    }

    /// <summary>
    /// Verifies the repository contains no warning or nullable suppression escape hatches.
    /// </summary>
    [Fact]
    public void RepositoryContainsNoWarningSuppressionEscapeHatches()
    {
        string root = FindRepositoryRoot();
        Regex forbiddenPattern = CreateForbiddenSuppressionPattern();
        var violations = new List<string>();

        foreach (string path in EnumerateSuppressionScanFiles(root))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (string.Equals(Path.GetFileName(path), "Global" + "Suppressions.cs", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: GlobalSuppressions.cs files are forbidden.");
                continue;
            }

            string text = File.ReadAllText(path);
            Match match = forbiddenPattern.Match(text);
            if (match.Success)
            {
                violations.Add($"{relativePath}: forbidden token '{match.Value}'.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies the repository contains no skipped xUnit tests.
    /// </summary>
    [Fact]
    public void RepositoryContainsNoSkippedTests()
    {
        string root = FindRepositoryRoot();
        Regex skippedTestPattern = CreateSkippedTestPattern();
        var violations = new List<string>();

        foreach (string path in EnumerateTestSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            Match match = skippedTestPattern.Match(text);
            if (match.Success)
            {
                string relativePath = Path.GetRelativePath(root, path);
                violations.Add($"{relativePath}: skipped test attribute '{match.Value}'.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies the upstream lockfile has been vendored into Scout.
    /// </summary>
    [Fact]
    public void UpstreamCargoLockIsVendored()
    {
        string root = FindRepositoryRoot();
        string cargoLock = File.ReadAllText(Path.Combine(root, "upstream", "Cargo.lock"));

        Assert.Contains("name = \"regex-automata\"", cargoLock, StringComparison.Ordinal);
        Assert.Contains("version = \"0.4.13\"", cargoLock, StringComparison.Ordinal);
        Assert.Contains("name = \"regex-syntax\"", cargoLock, StringComparison.Ordinal);
        Assert.Contains("version = \"0.8.8\"", cargoLock, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies PCRE2 provenance is recorded and matches the vendored <c>pcre2-sys</c> header.
    /// </summary>
    [Fact]
    public void Pcre2UpstreamPinMatchesVendoredHeader()
    {
        string root = FindRepositoryRoot();
        string upstream = File.ReadAllText(Path.Combine(root, "native", "pcre2", "UPSTREAM"));
        string sourceRoot = Path.Combine(root, "native", "pcre2", "pcre2-10.46");
        string buildScript = File.ReadAllText(Path.Combine(root, "native", "pcre2", "build-unix.sh"));
        string appBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-unix.sh"));
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string headerPath = Path.Combine(sourceRoot, "include", "pcre2.h");

        Assert.True(File.Exists(headerPath), "Missing vendored pcre2.h: " + headerPath);
        Assert.True(File.Exists(Path.Combine(sourceRoot, "src", "pcre2_compile.c")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "src", "pcre2_match.c")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "src", "pcre2_jit_compile.c")));
        string header = File.ReadAllText(headerPath);

        Assert.Contains("binding = \"pcre2\"", upstream, StringComparison.Ordinal);
        Assert.Contains("binding_version = \"0.2.11\"", upstream, StringComparison.Ordinal);
        Assert.Contains("sys_crate = \"pcre2-sys\"", upstream, StringComparison.Ordinal);
        Assert.Contains("sys_crate_version = \"0.2.10\"", upstream, StringComparison.Ordinal);
        Assert.Contains("c_library_tag = \"pcre2-10.46\"", upstream, StringComparison.Ordinal);
        Assert.Contains("c_library_commit_sha = \"" + PinnedPcre2Commit + "\"", upstream, StringComparison.Ordinal);
        Assert.Contains("commit_sha = \"" + PinnedPcre2Commit + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("#define PCRE2_MAJOR           10", header, StringComparison.Ordinal);
        Assert.Contains("#define PCRE2_MINOR           46", header, StringComparison.Ordinal);
        Assert.Contains("#define PCRE2_DATE            2025-08-27", header, StringComparison.Ordinal);
        Assert.Contains("pcre2-10.46", buildScript, StringComparison.Ordinal);
        Assert.Contains("-DPCRE2_CODE_UNIT_WIDTH=8", buildScript, StringComparison.Ordinal);
        Assert.Contains("-DPCRE2_STATIC=1", buildScript, StringComparison.Ordinal);
        Assert.Contains("-DSUPPORT_PCRE2_8=1", buildScript, StringComparison.Ordinal);
        Assert.Contains("-DSUPPORT_UNICODE=1", buildScript, StringComparison.Ordinal);
        Assert.Contains("-DSUPPORT_JIT=1", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"$ROOT/native/pcre2/build-unix.sh\" \"$RID\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("artifacts/native/pcre2/$RID/lib/libpcre2-8.a", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-Wl,-force_load,\"$PCRE2_LIB\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("_pcre2_config_8 _pcre2_compile_8 _pcre2_match_8", appBuildScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the regex conformance corpus files are pinned by hash.
    /// </summary>
    [Fact]
    public void RegexCorpusFilesMatchPinnedHashes()
    {
        (string Name, string Path, string Sha256)[] corpora =
        [
            (
                "regex-1.12.2-misc",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/misc.toml",
                "32C9591655C6FB118DFEFCB4DE49A04820A63CB960533DFC2538CDAABF4F4047"),
            (
                "regex-1.12.2-flags",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/flags.toml",
                "9A7E001808195C84F2A7D3E18BC0A82C7386E60F03A616E99AF00C3F7F2C3FD4"),
            (
                "regex-1.12.2-iter",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/iter.toml",
                "6875460302974A5B3073A7304A865C45ABA9653C54AFEA2C4D26E1EA248A81F7"),
            (
                "regex-1.12.2-empty",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/empty.toml",
                "738DBE92FBD8971385A1CF3AFFB0E956E5B692C858B9B48439D718F10801C08E"),
            (
                "regex-1.12.2-crazy",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/crazy.toml",
                "A146E2D2E23F1A57168979D9B1FC193C2BA38DCA66294B61140D6D2A2958EC86"),
            (
                "regex-1.12.2-multiline",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/multiline.toml",
                "EB07CF5427E6DDBCF61F4CC64C2D74FF41B5EF75EF857959651B20196F3CD157"),
            (
                "regex-1.12.2-line-terminator",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/line-terminator.toml",
                "02148068137B69D95587966917BDF0697BF7EB41AD6D47387F2EB30F67D04FD9"),
            (
                "regex-1.12.2-anchored",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/anchored.toml",
                "7A1B5CD81DEED2099796A451BF764A3F9BD21F0D60C0FA46ACCD3A35666866F2"),
            (
                "regex-1.12.2-substring",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/substring.toml",
                "48122D9F3477ED81F95E3AD42C06E9BB25F849B66994601A75CEAE0693B81866"),
            (
                "regex-1.12.2-bytes",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/bytes.toml",
                "1D84179165FD25F3B94BD2BFBEB43FC8A162041F7BF98B717E0F85CEF7FB652B"),
            (
                "regex-1.12.2-crlf",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/crlf.toml",
                "D19CF22756434D145DD20946C00AF01C102A556A252070405C3C8294129D9ECE"),
            (
                "regex-1.12.2-earliest",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/earliest.toml",
                "D561E643623EE1889B5B049FDCF3C7CB71B0C746D7EB822DDBD09D0ACDA2620B"),
            (
                "regex-1.12.2-expensive",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/expensive.toml",
                "5CE2F60209C99CDD2CDCB9D3069D1D5CA13D5E08A85E913EFE57267B2F5F0E9D"),
            (
                "regex-1.12.2-leftmost-all",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/leftmost-all.toml",
                "903BFBEFF888B7664296F4D5AA367CE53D1DAFE249AB0A3359223AE94D596396"),
            (
                "regex-1.12.2-no-unicode",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/no-unicode.toml",
                "D209DA04506900FD5F69E48170CDDAAD0702355AC6176C3A75AB3FF96974457C"),
            (
                "regex-1.12.2-overlapping",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/overlapping.toml",
                "5D96497A7233566D40B05BA22047E483FA8662E45515A9BE86DA45CF6C28703A"),
            (
                "regex-1.12.2-regex-lite",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/regex-lite.toml",
                "FECCA7CC8C9CEA2E1F84F846A89FD9B3CA7011C83698211A2EEDA8924DEB900C"),
            (
                "regex-1.12.2-regression",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/regression.toml",
                "6006EF4FCFBFD7155CE5CE8B8427904F7261C5549396F20CB065C0294733686D"),
            (
                "regex-1.12.2-set",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/set.toml",
                "DFD265DC1AEE80026E881616840DF0236AE9ABF12467D7EC0E141A52C236128C"),
            (
                "regex-1.12.2-unicode",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/unicode.toml",
                "7E4B013039B0CDD85FA73F32D15D096182FE901643D4E40C0910087A736CD46D"),
            (
                "regex-1.12.2-utf8",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/utf8.toml",
                "2EABCE0582BCACB2073E08BBE7CA413F096D14D06E917B107949691E24F84B20"),
            (
                "regex-1.12.2-word-boundary-special",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/word-boundary-special.toml",
                "7D0EA2F796478D1CA2A6954430CB1CFBD04031A182F8611CB50A7C73E443CE33"),
            (
                "regex-1.12.2-word-boundary",
                "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/regex-1.12.2/testdata/word-boundary.toml",
                "51BC1C498AB825420340A2DD3E6623DE4054937BA6D5020FF8CD14B1C1E45271"),
        ];

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        for (int index = 0; index < corpora.Length; index++)
        {
            (string name, string path, string expectedSha256) = corpora[index];
            Assert.Contains("name = \"" + name + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("sha256 = \"" + expectedSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);

            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            Assert.Equal(expectedSha256, Convert.ToHexString(hash));
        }
    }

    private static void AssertPackageVersion(XDocument document, string packageId, string expectedVersion)
    {
        foreach (XElement element in document.Descendants("PackageVersion"))
        {
            string? include = element.Attribute("Include")?.Value;
            if (string.Equals(include, packageId, StringComparison.Ordinal))
            {
                Assert.Equal(expectedVersion, element.Attribute("Version")?.Value);
                return;
            }
        }

        throw new InvalidOperationException($"Package '{packageId}' was not found.");
    }

    private static (int ExitCode, string Output, string Error) RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        for (int index = 0; index < arguments.Count; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start " + fileName + ".");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(fileName + " timed out.");
        }

        return (process.ExitCode, output, error);
    }

    private static Regex CreateForbiddenSuppressionPattern()
    {
        string[] patterns =
        [
            Regex.Escape("#pragma warning " + "disable"),
            @"\[\s*(Unconditional)?" + "Suppress" + @"Message\b",
            @"<\s*No" + "Warn" + @"\b",
            @"<\s*Warnings" + "NotAsErrors" + @"\b",
            @"<\s*Disabled" + "Warnings" + @"\b",
            Regex.Escape("#nullable " + "disable"),
            @"dotnet_diagnostic\.[^\r\n]*severity\s*=\s*(none|silent)\b",
            @"dotnet_diagnostic\.(SCOUT000[1-3]|IDE0130)\.severity\s*=\s*(?!error\b)[^\s#;]+",
            @"dotnet_analyzer_diagnostic\.category-Scout\.Structure\.severity\s*=\s*(?!error\b)[^\s#;]+",
        ];

        return new Regex(string.Join("|", patterns), RegexOptions.CultureInvariant);
    }

    private static Regex CreateSkippedTestPattern()
    {
        string pattern = Regex.Escape("[") + @"\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?:Fact|Theory)\s*\([^)\r\n]*\bSkip\s*=";
        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> EnumerateSuppressionScanFiles(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (ContainsPathSegment(relativePath, "bin") || ContainsPathSegment(relativePath, ".git"))
            {
                continue;
            }

            if (IsSuppressionScanFile(path))
            {
                yield return path;
            }
        }
    }

    private static bool ContainsPathSegment(string relativePath, string segment)
    {
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Array.Exists(parts, part => string.Equals(part, segment, StringComparison.Ordinal));
    }

    private static bool IsSuppressionScanFile(string path)
    {
        string fileName = Path.GetFileName(path);
        if (string.Equals(fileName, ".editorconfig", StringComparison.Ordinal) ||
            string.Equals(fileName, ".globalconfig", StringComparison.Ordinal) ||
            string.Equals(fileName, "Global" + "Suppressions.cs", StringComparison.Ordinal))
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".cs", StringComparison.Ordinal) ||
            string.Equals(extension, ".props", StringComparison.Ordinal) ||
            string.Equals(extension, ".targets", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateTestSourceFiles(string root)
    {
        string testsRoot = Path.Combine(root, "tests");
        foreach (string path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (!ContainsPathSegment(relativePath, "bin") && !ContainsPathSegment(relativePath, "obj"))
            {
                yield return path;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
