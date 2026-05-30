using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scout;

/// <summary>
/// Verifies the repository-level pins required by the Scout design.
/// </summary>
public sealed class PinnedConfigurationTests
{
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
