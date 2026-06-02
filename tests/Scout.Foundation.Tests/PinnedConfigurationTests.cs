using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scout;

/// <summary>
/// Verifies the repository-level pins required by the Scout design.
/// </summary>
public sealed partial class PinnedConfigurationTests
{
    private const string PinnedRipgrepCommit = "4857d6fa67db69a95cd4b6f2adda5d807d4d0119";
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
    /// Verifies CI encodes the cross-platform RID and release-gate commands required by the design.
    /// </summary>
    [Fact]
    public void CiWorkflowPinsCrossPlatformGates()
    {
        string root = FindRepositoryRoot();
        string workflowPath = Path.Combine(root, ".github", "workflows", "ci.yml");
        string releaseGateWorkflowPath = Path.Combine(root, ".github", "workflows", "release-gates.yml");

        Assert.True(File.Exists(workflowPath), "Missing CI workflow: " + workflowPath);
        Assert.True(File.Exists(releaseGateWorkflowPath), "Missing release gate workflow: " + releaseGateWorkflowPath);
        string ciWorkflow = File.ReadAllText(workflowPath);
        string releaseGateWorkflow = File.ReadAllText(releaseGateWorkflowPath);
        string benchmarkReadme = File.ReadAllText(Path.Combine(root, "bench", "README.md"));
        string workflow = ciWorkflow + "\n" + releaseGateWorkflow;
        string[] githubHostedRunnerLabels =
        [
            "ubuntu-24.04",
            "ubuntu-24.04-arm",
            "macos-26-intel",
            "macos-26",
            "windows-2025-vs2026",
            "windows-11-arm",
        ];

        Assert.Contains("push:", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("pull_request:", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_run", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("release gates dispatch", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run release-gates.yml --repo \"${{ github.repository }}\" --ref main -f checkout_ref=\"${{ github.sha }}\"", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("checkout_ref:", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("LINUX_LIBC_VERSION: \"2.36-9+deb12u14\"", workflow, StringComparison.Ordinal);
        Assert.Contains("LINUX_SNAPSHOT_URL: \"http://snapshot.debian.org/archive/debian/20260531T000000Z\"", workflow, StringComparison.Ordinal);
        Assert.Contains("image: docker.io/library/debian:bookworm-slim@sha256:0104b334637a5f19aa9c983a91b54c89887c0984081f2068983107a6f6c21eeb", workflow, StringComparison.Ordinal);
        Assert.Contains("Pinned snapshot apt prerequisites", workflow, StringComparison.Ordinal);
        Assert.Contains("rm -f /etc/apt/sources.list.d/*.sources /etc/apt/sources.list.d/*.list", workflow, StringComparison.Ordinal);
        Assert.Contains("printf 'deb %s bookworm main\\n' \"$LINUX_SNAPSHOT_URL\" > /etc/apt/sources.list", workflow, StringComparison.Ordinal);
        Assert.Contains("Acquire::Check-Valid-Until false", workflow, StringComparison.Ordinal);
        Assert.Contains("apt-get install -y --no-install-recommends", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--allow-downgrades", workflow, StringComparison.Ordinal);
        Assert.Contains("libc6=\"$LINUX_LIBC_VERSION\"", workflow, StringComparison.Ordinal);
        Assert.Contains("libc-bin=\"$LINUX_LIBC_VERSION\"", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/checkout@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/setup-dotnet@v5", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.102", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet build Scout.slnx --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("Portable tests", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Scout.Regex.Tests/Scout.Regex.Tests.csproj --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Scout.Foundation.Tests/Scout.Foundation.Tests.csproj --no-restore --filter \"FullyQualifiedName!~ScoutApplicationTests&FullyQualifiedName!~ScoutApplicationRuntimeTests&FullyQualifiedName!~PinnedConfigurationTests\"", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Scout.Differential.Tests/Scout.Differential.Tests.csproj --no-restore --filter \"FullyQualifiedName~DifferentialCasePolicyTests|FullyQualifiedName~DifferentialOutputNormalizerTests\"", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project fuzz/Scout.Fuzz/Scout.Fuzz.csproj --no-build -- regex-parse", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project fuzz/Scout.Fuzz/Scout.Fuzz.csproj --no-build -- glob-compile", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project fuzz/Scout.Fuzz/Scout.Fuzz.csproj --no-build -- search-loop", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test Scout.slnx --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format Scout.slnx --no-restore --verify-no-changes", workflow, StringComparison.Ordinal);
        Assert.Contains("MSBuild warning gates", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/check-msbuild-warning-gates.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-linux-prereqs.sh ${{ matrix.rid }}", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-arm64-host", workflow, StringComparison.Ordinal);
        Assert.Contains("Linux ARM hosted gates", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/install-linux-host-prereqs.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/preflight.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ inputs.checkout_ref || github.sha }}", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("Build pinned ripgrep oracle", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/setup-ripgrep-oracle.sh", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("full pinned tests (${{ matrix.rid }})", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("full pinned tests (osx-arm64)", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("release native Linux gate (linux-x64)", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("release native Linux gate (linux-arm64)", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("release native macOS gate (${{ matrix.rid }})", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("release native Windows gate (${{ matrix.rid }})", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh linux-x64 --smoke-only", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh linux-arm64 --smoke-only", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh ${{ matrix.rid }} --smoke-only", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-windows.ps1 ${{ matrix.rid }}", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("spike/build-unix.sh ${{ matrix.rid }}", workflow, StringComparison.Ordinal);
        Assert.Contains("spike/build-unix.sh linux-x64", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("spike/build-unix.sh linux-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("spike/build-windows.ps1 ${{ matrix.rid }}", workflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh ${{ matrix.rid }} --smoke-only", workflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh ${{ matrix.rid }} --with-differentials", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-unix.sh osx-arm64 --with-differentials", workflow, StringComparison.Ordinal);
        Assert.Contains("native/build-app-windows.ps1 ${{ matrix.rid }}", workflow, StringComparison.Ordinal);
        Assert.Contains("vsarch: amd64", workflow, StringComparison.Ordinal);
        Assert.Contains("vsarch: arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04-arm", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: macos-26-intel", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: macos-26", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: windows-2025-vs2026", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: windows-11-arm", workflow, StringComparison.Ordinal);
        Assert.Contains("bench/run-hyperfine.sh --gate", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: macos-26", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("Install pinned hyperfine", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/setup-hyperfine.sh", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("GitHub-hosted", benchmarkReadme, StringComparison.Ordinal);
        for (int index = 0; index < githubHostedRunnerLabels.Length; index++)
        {
            Assert.Contains(githubHostedRunnerLabels[index], benchmarkReadme, StringComparison.Ordinal);
        }

        Assert.Contains("exact successful commit SHA", benchmarkReadme, StringComparison.Ordinal);
        Assert.Contains("pinned release-LTO `rg` oracle", benchmarkReadme, StringComparison.Ordinal);
        Assert.Contains("stale CI", benchmarkReadme, StringComparison.Ordinal);
        Assert.Contains("It does not require any personal machine", benchmarkReadme, StringComparison.Ordinal);
        Assert.DoesNotContain("clean: false", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_GATES_RUNNER_TOKEN", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", ciWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", benchmarkReadme, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_GATES_RUNNER_TOKEN", benchmarkReadme, StringComparison.Ordinal);
        Assert.Contains("libicu72", workflow, StringComparison.Ordinal);
        Assert.Contains("zlib1g-dev", workflow, StringComparison.Ordinal);
        Assert.Contains("ncompress", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("macos-13", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("macos-15", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("brew install hyperfine", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PublishAot=true", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error: true", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("if: github.event_name == 'workflow_dispatch'", workflow, StringComparison.Ordinal);

        foreach (string runnerLabel in EnumerateWorkflowRunnerLabels(workflow))
        {
            Assert.True(
                Array.Exists(githubHostedRunnerLabels, label => string.Equals(label, runnerLabel, StringComparison.Ordinal)),
                "Workflow runner label must be GitHub-hosted and pinned by CiWorkflowPinsCrossPlatformGates: " + runnerLabel);
        }

        string[] requiredRids =
        [
            "linux-x64",
            "linux-arm64",
            "osx-x64",
            "osx-arm64",
            "win-x64",
            "win-arm64",
        ];
        for (int index = 0; index < requiredRids.Length; index++)
        {
            Assert.Contains(requiredRids[index], workflow, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies Git checkout preserves LF source files for cross-platform format gates.
    /// </summary>
    [Fact]
    public void RepositoryPinsCrossPlatformLineEndings()
    {
        string root = FindRepositoryRoot();
        string attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));

        Assert.Contains("* text=auto eol=lf", attributes, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the local ripgrep reference checkout is at the design pin.
    /// </summary>
    [Fact]
    public void ReferenceRipgrepCheckoutMatchesPinnedCommit()
    {
        string referenceRipgrepRoot = PinnedRipgrepOracle.ReferenceRoot;

        Assert.True(Directory.Exists(referenceRipgrepRoot), "Missing reference checkout: " + referenceRipgrepRoot);

        (int exitCode, string output, string error) = RunProcess("git", ["-C", referenceRipgrepRoot, "rev-parse", "HEAD"]);

        Assert.True(exitCode == 0, error);
        Assert.Equal(PinnedRipgrepCommit, output.Trim());
    }

    /// <summary>
    /// Verifies hosted release gates provision the pinned ripgrep oracle before any parity checks run.
    /// </summary>
    [Fact]
    public void HostedReleaseGatesProvisionPinnedRipgrepOracle()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-gates.yml"));
        string script = File.ReadAllText(Path.Combine(root, "eng", "setup-ripgrep-oracle.sh"));

        Assert.Contains("Build pinned ripgrep oracle", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/setup-ripgrep-oracle.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("SCOUT_RIPGREP_REFERENCE", script, StringComparison.Ordinal);
        Assert.Contains("host_rid()", script, StringComparison.Ordinal);
        Assert.Contains("oracle_environment()", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_rid_table_value()", script, StringComparison.Ordinal);
        Assert.Contains("derive_reference_from_oracle_path()", script, StringComparison.Ordinal);
        Assert.Contains("ripgrep_commit", script, StringComparison.Ordinal);
        Assert.Contains("ripgrep_rg_sha256", script, StringComparison.Ordinal);
        Assert.Contains("ripgrep_pcre2_rg_sha256", script, StringComparison.Ordinal);
        Assert.Contains("ripgrep_oracle", script, StringComparison.Ordinal);
        Assert.Contains("rustup toolchain install \"$RUST_TOOLCHAIN\" --profile minimal", script, StringComparison.Ordinal);
        Assert.Contains("cargo \"+$RUST_TOOLCHAIN\" build --profile \"$RG_PROFILE\" --bin rg", script, StringComparison.Ordinal);
        Assert.Contains("CARGO_TARGET_DIR=\"$REFERENCE/target/pcre2\"", script, StringComparison.Ordinal);
        Assert.Contains("PCRE2_SYS_STATIC=1", script, StringComparison.Ordinal);
        Assert.Contains("verify_binary_hash \"reference rg\"", script, StringComparison.Ordinal);
        Assert.Contains("verify_binary_hash \"PCRE2 reference rg\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies hosted oracle capture can produce frozen lockfile rows without relying on private runners.
    /// </summary>
    [Fact]
    public void HostedOracleCaptureUsesGitHubHostedUnixRunners()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "oracle-capture.yml"));
        string script = File.ReadAllText(Path.Combine(root, "eng", "capture-ripgrep-oracle.sh"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("capture ripgrep oracle (linux-x64)", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("image: docker.io/library/debian:bookworm-slim@sha256:0104b334637a5f19aa9c983a91b54c89887c0984081f2068983107a6f6c21eeb", workflow, StringComparison.Ordinal);
        Assert.Contains("LINUX_SNAPSHOT_URL", workflow, StringComparison.Ordinal);
        Assert.Contains("LINUX_LIBC_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("capture ripgrep oracle (linux-arm64)", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04-arm", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "capture ripgrep oracle (linux-arm64)\n    runs-on: ubuntu-24.04-arm\n    timeout-minutes: 90\n    container:\n      image: docker.io/library/debian:bookworm-slim@sha256:0104b334637a5f19aa9c983a91b54c89887c0984081f2068983107a6f6c21eeb",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("runner: macos-26-intel", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: macos-26", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/capture-ripgrep-oracle.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_GATES_RUNNER_TOKEN", workflow, StringComparison.Ordinal);

        Assert.Contains("host_rid()", script, StringComparison.Ordinal);
        Assert.Contains("oracle_environment()", script, StringComparison.Ordinal);
        Assert.Contains("artifacts/ripgrep-oracle/$HOST_RID/ripgrep", script, StringComparison.Ordinal);
        Assert.Contains("cargo \"+$RUST_TOOLCHAIN\" build --profile \"$RG_PROFILE\" --bin rg", script, StringComparison.Ordinal);
        Assert.Contains("PCRE2_SYS_STATIC=1", script, StringComparison.Ordinal);
        Assert.Contains("sha256_file \"$RG_PATH\"", script, StringComparison.Ordinal);
        Assert.Contains("[[ripgrep_oracle]]", script, StringComparison.Ordinal);
        Assert.Contains("pcre2_sha256", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the pinned ripgrep binary exists and reports the pinned revision.
    /// </summary>
    [Fact]
    public void PinnedRipgrepBinaryMatchesPinnedRevision()
    {
        string pinnedRipgrepBinaryPath = PinnedRipgrepOracle.ExecutablePath;
        Assert.True(File.Exists(pinnedRipgrepBinaryPath), "Missing pinned ripgrep binary: " + pinnedRipgrepBinaryPath);

        (int exitCode, string output, string error) = RunProcess(pinnedRipgrepBinaryPath, ["--version"]);

        Assert.True(exitCode == 0, error);
        Assert.StartsWith("ripgrep 15.1.0 (rev 4857d6fa67)", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the pinned ripgrep binary hash matches the recorded differential oracle.
    /// </summary>
    [Fact]
    public void PinnedRipgrepBinaryMatchesPrerequisiteHash()
    {
        string pinnedRipgrepBinaryPath = PinnedRipgrepOracle.ExecutablePath;
        Assert.True(File.Exists(pinnedRipgrepBinaryPath), "Missing pinned ripgrep binary: " + pinnedRipgrepBinaryPath);
        PinnedRipgrepOracle.VerifyHash();

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string defaultExecutablePath = PinnedRipgrepOracle.ReadHostOracleValue("path", "ripgrep_rg_path");
        string expectedSha256 = PinnedRipgrepOracle.ExpectedSha256;

        string oracleBlock = string.Join(
            "\n",
            "[[ripgrep_oracle]]",
            "rid = \"" + PinnedRipgrepOracle.HostRid + "\"",
            "environment = \"" + PinnedRipgrepOracle.HostOracleEnvironment + "\"",
            "profile = \"release-lto\"",
            "path = \"" + defaultExecutablePath + "\"",
            "sha256 = \"" + expectedSha256 + "\"");
        Assert.Contains(oracleBlock, prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("environment = \"github-actions\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("ripgrep_rg_profile = \"release-lto\"", prerequisiteLock, StringComparison.Ordinal);
        if (string.Equals(PinnedRipgrepOracle.HostOracleEnvironment, "local", StringComparison.Ordinal))
        {
            Assert.Contains("ripgrep_rg_path = \"" + defaultExecutablePath + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("ripgrep_rg_sha256 = \"" + expectedSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
        }
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
        AssertPackageVersion(document, "SharpFuzz", "2.2.0");
        AssertPackageVersion(document, "xunit.v3", "3.2.2");
    }

    /// <summary>
    /// Verifies the design-required fuzzing layer is wired into the solution.
    /// </summary>
    [Fact]
    public void FuzzHarnessPinsDesignTargets()
    {
        string root = FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(root, "Scout.slnx"));
        string project = File.ReadAllText(Path.Combine(root, "fuzz", "Scout.Fuzz", "Scout.Fuzz.csproj"));
        string runner = File.ReadAllText(Path.Combine(root, "fuzz", "Scout.Fuzz", "FuzzTargetRunner.cs"));

        Assert.Contains("fuzz/Scout.Fuzz/Scout.Fuzz.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"SharpFuzz\" />", project, StringComparison.Ordinal);
        Assert.Contains("RegexParseFuzzTarget.Run", runner, StringComparison.Ordinal);
        Assert.Contains("GlobCompileFuzzTarget.Run", runner, StringComparison.Ordinal);
        Assert.Contains("SearchLoopFuzzTarget.Run", runner, StringComparison.Ordinal);
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
    /// Verifies CI evaluates warning gates after MSBuild imports are applied.
    /// </summary>
    [Fact]
    public void MsBuildWarningGateEvaluatesImportedProperties()
    {
        string root = FindRepositoryRoot();
        string preflight = File.ReadAllText(Path.Combine(root, "eng", "preflight.sh"));
        string script = File.ReadAllText(Path.Combine(root, "eng", "check-msbuild-warning-gates.sh"));

        Assert.Contains("artifacts/preflight/msbuild-warning-gates", preflight, StringComparison.Ordinal);
        Assert.Contains("-getProperty:NoWarn", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:WarningsNotAsErrors", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:TreatWarningsAsErrors", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:MSBuildTreatWarningsAsErrors", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:AnalysisLevel", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:AnalysisMode", script, StringComparison.Ordinal);
        Assert.Contains("-getProperty:EnforceCodeStyleInBuild", script, StringComparison.Ordinal);
        Assert.Contains("-getItem:EditorConfigFiles", script, StringComparison.Ordinal);
        Assert.Contains("scan_repository_suppression_files", script, StringComparison.Ordinal);
        Assert.Contains("repository-suppression-scan.txt", script, StringComparison.Ordinal);
        Assert.Contains("GlobalSuppressions.cs files are forbidden", script, StringComparison.Ordinal);
        Assert.Contains("forbidden repository suppression token", script, StringComparison.Ordinal);
        Assert.Contains("json_item_full_paths", script, StringComparison.Ordinal);
        Assert.Contains("scan_editor_config_file", script, StringComparison.Ordinal);
        Assert.Contains("check_evaluated_editor_config_files", script, StringComparison.Ordinal);
        Assert.Contains("-noAutoResponse", script, StringComparison.Ordinal);
        Assert.Contains("check_raw_nowarn", script, StringComparison.Ordinal);
        Assert.Contains("write_property \"RawNoWarn\"", script, StringComparison.Ordinal);
        Assert.Contains("check_empty \"$relative_project\" \"NoWarn\"", script, StringComparison.Ordinal);
        Assert.Contains("check_empty \"$relative_project\" \"WarningsNotAsErrors\"", script, StringComparison.Ordinal);
        Assert.Contains("check_true \"$relative_project\" \"TreatWarningsAsErrors\"", script, StringComparison.Ordinal);
        Assert.Contains("check_true \"$relative_project\" \"MSBuildTreatWarningsAsErrors\"", script, StringComparison.Ordinal);
        Assert.Contains("dotnet_analyzer_diagnostic\\.category-Scout\\.Structure\\.severity", script, StringComparison.Ordinal);
        Assert.Contains("SCOUT[0-9]+", script, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic\\.SCOUT0004\\.severity", script, StringComparison.Ordinal);
        Assert.Contains("none|silent", script, StringComparison.Ordinal);

        string responseFile = File.ReadAllText(Path.Combine(root, "Directory.Build.rsp"));
        Assert.Contains("-p:NoWarn=", responseFile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the repository contains no skipped, ignored, explicit, or quarantined tests.
    /// </summary>
    [Fact]
    public void RepositoryContainsNoSkippedIgnoredOrQuarantinedTests()
    {
        string root = FindRepositoryRoot();
        (string Label, Regex Pattern)[] forbiddenPatterns = CreateForbiddenTestWaiverPatterns();
        var violations = new List<string>();

        foreach (string path in EnumerateTestSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            foreach ((string label, Regex pattern) in forbiddenPatterns)
            {
                Match match = pattern.Match(text);
                if (match.Success)
                {
                    string relativePath = Path.GetRelativePath(root, path);
                    violations.Add($"{relativePath}: {label} '{match.Value}'.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies the build fails on every test waiver form forbidden by the design.
    /// </summary>
    [Fact]
    public void SourceAnalyzerRejectsSkippedIgnoredExplicitAndQuarantinedTests()
    {
        string root = FindRepositoryRoot();
        string analyzer = File.ReadAllText(Path.Combine(root, "src", "Scout.SourceGen", "NoSkippedTestsAnalyzer.cs"));
        string descriptors = File.ReadAllText(Path.Combine(root, "src", "Scout.SourceGen", "DiagnosticDescriptors.cs"));
        string editorConfig = File.ReadAllText(Path.Combine(root, ".editorconfig"));

        Assert.Contains("NoSkippedTestsAnalyzer", analyzer, StringComparison.Ordinal);
        Assert.Contains("GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics", analyzer, StringComparison.Ordinal);
        Assert.Contains("build_property.IsTestProject", analyzer, StringComparison.Ordinal);
        Assert.Contains("TestWaiverIsForbidden", descriptors, StringComparison.Ordinal);
        Assert.Contains("SCOUT0004", descriptors, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic." + "SCOUT0004.severity = " + "error", editorConfig, StringComparison.Ordinal);
        Assert.Contains("Fact", analyzer, StringComparison.Ordinal);
        Assert.Contains("Theory", analyzer, StringComparison.Ordinal);
        Assert.Contains("Trait", analyzer, StringComparison.Ordinal);
        Assert.Contains("TryCreateDirectorySymlink", analyzer, StringComparison.Ordinal);
        Assert.Contains("TryCreateFileSymlink", analyzer, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.", analyzer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the parity ledger carries no release waivers.
    /// </summary>
    [Fact]
    public void ParityLedgerContainsNoTrackedGaps()
    {
        string root = FindRepositoryRoot();
        string parity = File.ReadAllText(Path.Combine(root, "docs", "PARITY.md"));

        Assert.Contains("Scout has no accepted runtime deviations from the pinned ripgrep behavior.", parity, StringComparison.Ordinal);
        Assert.Equal("None.", ReadMarkdownSection(parity, "Tracked Gaps").Trim());
    }

    /// <summary>
    /// Verifies project provenance files do not carry deferred-scope language.
    /// </summary>
    [Fact]
    public void ProjectProvenanceFilesContainNoDeferralLanguage()
    {
        string root = FindRepositoryRoot();
        Regex forbiddenPattern = CreateForbiddenProjectProvenanceDeferralPattern();
        var violations = new List<string>();

        foreach (string path in EnumerateProjectProvenanceFiles(root))
        {
            string text = File.ReadAllText(path);
            Match match = forbiddenPattern.Match(text);
            if (match.Success)
            {
                string relativePath = Path.GetRelativePath(root, path);
                violations.Add($"{relativePath}: forbidden deferral language '{match.Value}'.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies generated flag definitions live in a dedicated definitions folder.
    /// </summary>
    [Fact]
    public void GeneratedFlagDefinitionsUseDedicatedFolder()
    {
        string root = FindRepositoryRoot();
        string appRoot = Path.Combine(root, "src", "Scout.App");
        string definitionsRoot = Path.Combine(appRoot, "Flags", "Definitions");
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(appRoot, "*Flag.cs", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(Path.GetFileName(path), "IFlag.cs", StringComparison.Ordinal))
            {
                violations.Add(Path.GetRelativePath(root, path) + " is not under Flags/Definitions.");
            }
        }

        string[] definitionFiles = Directory.GetFiles(definitionsRoot, "*Flag.cs", SearchOption.TopDirectoryOnly);
        Assert.Equal(GeneratedFlagCatalog.Descriptors.Length, definitionFiles.Length);
        Assert.Equal(104, definitionFiles.Length);

        var orders = new HashSet<int>();
        foreach (string path in definitionFiles)
        {
            string text = File.ReadAllText(path);
            if (!text.Contains("namespace Scout.Flags.Definitions;", StringComparison.Ordinal))
            {
                violations.Add(Path.GetRelativePath(root, path) + " does not use the flag-definition namespace.");
            }

            if (!text.Contains(" : IFlag<", StringComparison.Ordinal))
            {
                violations.Add(Path.GetRelativePath(root, path) + " does not implement IFlag<TSelf>.");
            }

            if (!TryReadFlagOrder(text, out int order))
            {
                violations.Add(Path.GetRelativePath(root, path) + " does not declare [FlagOrder(<pinned upstream index>)].");
            }
            else if (!orders.Add(order))
            {
                violations.Add(Path.GetRelativePath(root, path) + " duplicates flag order " + order + ".");
            }
        }

        for (int order = 0; order < definitionFiles.Length; order++)
        {
            if (!orders.Contains(order))
            {
                violations.Add("Missing flag order " + order + ".");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Verifies the flag catalog generator reads ordering from flag definitions.
    /// </summary>
    [Fact]
    public void FlagCatalogGeneratorReadsOrderFromDefinitions()
    {
        string root = FindRepositoryRoot();
        string generator = File.ReadAllText(Path.Combine(root, "src", "Scout.SourceGen", "FlagCatalogSourceGenerator.cs"));

        Assert.Contains("FlagOrderAttribute", generator, StringComparison.Ordinal);
        Assert.Contains("CompareByFlagDefinitionOrder", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPinnedUpstreamOrder", generator, StringComparison.Ordinal);
    }

    private static bool TryReadFlagOrder(string source, out int order)
    {
        const string Prefix = "[FlagOrder(";
        int start = source.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            order = -1;
            return false;
        }

        start += Prefix.Length;
        int end = source.IndexOf(')', start);
        if (end < start)
        {
            order = -1;
            return false;
        }

        return int.TryParse(source[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out order);
    }

    /// <summary>
    /// Verifies native interop uses source-generated marshalling.
    /// </summary>
    [Fact]
    public void NativeInteropUsesLibraryImport()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string sourceRoot in new[] { Path.Combine(root, "src"), Path.Combine(root, "spike") })
        {
            foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(root, path);
                if (ContainsPathSegment(relativePath, "bin") || ContainsPathSegment(relativePath, "obj"))
                {
                    continue;
                }

                string text = File.ReadAllText(path);
                if (text.Contains("[DllImport", StringComparison.Ordinal))
                {
                    violations.Add(relativePath);
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies the Unix OS layer exposes byte-preserving link target reads.
    /// </summary>
    [Fact]
    public void UnixOsLayerReadsLinkTargetsAsBytes()
    {
        string root = FindRepositoryRoot();
        string metadataPath = Path.Combine(root, "src", "Scout.Ignore", "NativeFileSystemMetadata.cs");
        string metadata = File.ReadAllText(metadataPath);

        Assert.Contains("TryReadRawUnixLinkTarget(ReadOnlySpan<byte> path, out byte[] target)", metadata, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"libc\", EntryPoint = \"readlink\", SetLastError = true)]", metadata, StringComparison.Ordinal);
        Assert.Contains("private static partial nint ReadLinkRaw(byte* path, byte* buffer, nuint bufferLength);", metadata, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies CLI utilities own external decompression and preprocessor process spawning.
    /// </summary>
    [Fact]
    public void CliUtilitiesOwnExternalSearchCommandSpawning()
    {
        string root = FindRepositoryRoot();
        string appReaderPath = Path.Combine(root, "src", "Scout.App", "SearchFileContentReader.cs");
        string cliRunnerPath = Path.Combine(root, "src", "Scout.Cli", "CliSearchCommandRunner.cs");

        Assert.True(File.Exists(cliRunnerPath), "Missing CLI command runner: " + cliRunnerPath);

        string appReader = File.ReadAllText(appReaderPath);
        string cliRunner = File.ReadAllText(cliRunnerPath);
        string cliUpstream = File.ReadAllText(Path.Combine(root, "src", "Scout.Cli", "UPSTREAM.md"));

        Assert.Contains("CliSearchCommandRunner.TryRun", appReader, StringComparison.Ordinal);
        Assert.DoesNotContain("new Process", appReader, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", appReader, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Diagnostics", appReader, StringComparison.Ordinal);
        Assert.Contains("new Process()", cliRunner, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", cliRunner, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = true", cliRunner, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardInput = pipeFileToStandardInput", cliRunner, StringComparison.Ordinal);
        Assert.Contains("decompression/preprocessor command matching and process spawning", cliUpstream, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies runtime regex behavior does not fall back to the UTF-16 BCL regex engine.
    /// </summary>
    [Fact]
    public void RuntimeRegexBehaviorDoesNotUseBclRegex()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (ContainsPathSegment(relativePath, "bin") || ContainsPathSegment(relativePath, "obj"))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            if (text.Contains("System.Text.RegularExpressions", StringComparison.Ordinal) ||
                text.Contains("[GeneratedRegex", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies <c>-E</c> decoding stays on Scout's <c>encoding_rs</c> port instead of BCL code pages.
    /// </summary>
    [Fact]
    public void SearchEncodingDoesNotUseSystemTextEncoding()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();
        string[] encodingProjects =
        [
            Path.Combine(root, "src", "Scout.Encoding"),
            Path.Combine(root, "src", "Scout.Encoding.Io"),
        ];

        foreach (string projectPath in encodingProjects)
        {
            foreach (string path in Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(root, path);
                if (ContainsPathSegment(relativePath, "bin") || ContainsPathSegment(relativePath, "obj"))
                {
                    continue;
                }

                string text = File.ReadAllText(path);
                if (text.Contains("System.Text.Encoding", StringComparison.Ordinal) ||
                    text.Contains("Encoding.GetEncoding", StringComparison.Ordinal) ||
                    text.Contains("CodePagesEncodingProvider", StringComparison.Ordinal) ||
                    text.Contains("Encoding.RegisterProvider", StringComparison.Ordinal))
                {
                    violations.Add(relativePath);
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies runtime JSON output remains a hand-written byte writer.
    /// </summary>
    [Fact]
    public void RuntimeJsonOutputDoesNotUseBclJson()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();
        string[] forbiddenTokens =
        [
            "System.Text.Json",
            "JsonSerializer",
            "Utf8JsonWriter",
            "JsonDocument",
            "JsonNode",
            "JsonObject",
            "JsonArray",
            "Newtonsoft.Json",
            "DataContractJsonSerializer",
            "JavaScriptSerializer",
        ];

        foreach (string path in EnumerateRuntimeSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            foreach (string token in forbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(root, path)}: {token}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies runtime code avoids reflection, <c>dynamic</c>, and runtime code generation.
    /// </summary>
    [Fact]
    public void RuntimeCodeDoesNotUseReflectionDynamicOrRuntimeCodegen()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();
        string[] forbiddenTokens =
        [
            "System.Reflection",
            "Activator.CreateInstance",
            "Type.GetType",
            ".GetType(",
            ".GetTypes(",
            ".GetMethods(",
            ".GetProperties(",
            ".GetFields(",
            ".GetConstructors(",
            "MakeGenericType",
            "MakeGenericMethod",
            "Assembly.Load",
            "dynamic ",
            "System.Linq.Expressions",
            "Expression.Compile",
            "Reflection.Emit",
            "RuntimeFeature.IsDynamicCodeSupported",
            "RuntimeFeature.IsDynamicCodeCompiled",
        ];

        foreach (string path in EnumerateRuntimeSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            foreach (string token in forbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(root, path)}: {token}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies runtime stdout/stderr paths do not use text-based <see cref="Console" /> writers.
    /// </summary>
    [Fact]
    public void RuntimeOutputDoesNotUseConsoleTextWriters()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();
        string[] forbiddenTokens =
        [
            "Console.Write",
            "Console.Out",
            "Console.Error",
            "Console.SetOut",
            "Console.SetError",
            "new StreamWriter(Console.OpenStandardOutput",
            "new StreamWriter(Console.OpenStandardError",
        ];

        foreach (string path in EnumerateRuntimeSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            foreach (string token in forbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(root, path)}: {token}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies runtime code routes environment variable reads through the byte-preserving OS layer.
    /// </summary>
    [Fact]
    public void RuntimeEnvironmentReadsUseProcessEnvironment()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (ContainsPathSegment(relativePath, "bin") ||
                ContainsPathSegment(relativePath, "obj") ||
                string.Equals(relativePath, Path.Combine("src", "Scout.Os", "ProcessEnvironment.cs"), StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            if (text.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Verifies native entry captures raw Unix bytes and Windows UTF-16 arguments at the OS boundary.
    /// </summary>
    [Fact]
    public void NativeEntryCapturesPlatformArgumentsAtBoundary()
    {
        string root = FindRepositoryRoot();
        string scoutEntry = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "ScoutEntry.cs"));
        string nativeArgumentReader = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "NativeArgumentReader.cs"));
        string unixEntry = File.ReadAllText(Path.Combine(root, "native", "entry", "scout_main.c"));
        string windowsEntry = File.ReadAllText(Path.Combine(root, "native", "entry", "scout_wmain.c"));
        string spikeProject = File.ReadAllText(Path.Combine(root, "spike", "Scout.Entry", "Scout.Entry.csproj"));
        string spikeEntry = File.ReadAllText(Path.Combine(root, "spike", "Scout.Entry", "ScoutEntry.cs"));
        string spikeBuildScript = File.ReadAllText(Path.Combine(root, "spike", "build-unix.sh"));
        string spikeUnixEntry = File.ReadAllText(Path.Combine(root, "spike", "native", "scout_main.c"));
        string spikeWindowsEntry = File.ReadAllText(Path.Combine(root, "spike", "native", "scout_wmain.c"));
        string spikeWindowsBuildScript = File.ReadAllText(Path.Combine(root, "spike", "build-windows.ps1"));
        string design = File.ReadAllText(Path.Combine(root, "docs", "DESIGN.md"));

        Assert.Contains("[UnmanagedCallersOnly(EntryPoint = \"scout_entry\")]", scoutEntry, StringComparison.Ordinal);
        Assert.Contains("NativeArgumentReader.CaptureUnix(argc, argv)", scoutEntry, StringComparison.Ordinal);
        Assert.Contains("NativeArgumentReader.CaptureWindowsCommandLine()", scoutEntry, StringComparison.Ordinal);
        Assert.Contains("ProcessEnvironment.UseUnixEnvironment(envp)", scoutEntry, StringComparison.Ordinal);
        Assert.Contains("GetCommandLineW", nativeArgumentReader, StringComparison.Ordinal);
        Assert.Contains("CommandLineToArgvW", nativeArgumentReader, StringComparison.Ordinal);
        Assert.Contains("LocalFree", nativeArgumentReader, StringComparison.Ordinal);
        Assert.Contains("OsString.FromUnixBytes", nativeArgumentReader, StringComparison.Ordinal);
        Assert.Contains("OsString.FromWindowsString", nativeArgumentReader, StringComparison.Ordinal);
        Assert.Contains("try_run_native_fast_path(argc, argv)", unixEntry, StringComparison.Ordinal);
        Assert.Contains("run_simple_tiny_search", unixEntry, StringComparison.Ordinal);
        Assert.Contains("return scout_entry(argc, argv, envp);", unixEntry, StringComparison.Ordinal);
        Assert.Contains("int wmain(int argc, wchar_t **argv, wchar_t **envp)", windowsEntry, StringComparison.Ordinal);
        Assert.Contains("return scout_entry(0, (char **)0, (char **)0);", windowsEntry, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>Scout.Entry</RootNamespace>", spikeProject, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"libc\", EntryPoint = \"write\")]", spikeEntry, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"kernel32.dll\", EntryPoint = \"GetCommandLineW\")]", spikeEntry, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"shell32.dll\", EntryPoint = \"CommandLineToArgvW\", SetLastError = true)]", spikeEntry, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"kernel32.dll\", EntryPoint = \"WriteFile\", SetLastError = true)]", spikeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("[DllImport", spikeEntry, StringComparison.Ordinal);
        Assert.Contains("osx-arm64|osx-x64|linux-x64|linux-arm64", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("libSystem.Security.Cryptography.Native.OpenSsl.a", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("-lstdc++ -lz -lpthread -ldl -lm -lrt", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("libRuntime.VxsortEnabled.a", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("printf '\\377\\n' > \"$OUT/expected\"", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("\"$OUT/scout-spike\" \"$(printf '\\377')\" > \"$OUT/got\"", spikeBuildScript, StringComparison.Ordinal);
        Assert.Contains("cmp \"$OUT/expected\" \"$OUT/got\"", spikeBuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("not implemented", spikeBuildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return scout_entry(argc, argv, envp);", spikeUnixEntry, StringComparison.Ordinal);
        Assert.Contains("int wmain(int argc, wchar_t **argv, wchar_t **envp)", spikeWindowsEntry, StringComparison.Ordinal);
        Assert.Contains("return scout_entry(0, (char **)0, (char **)0);", spikeWindowsEntry, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet(\"win-x64\", \"win-arm64\")]", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("Scout.Entry.lib", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/ENTRY:wmainCRTStartup", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("Runtime.VxsortEnabled.lib", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("[System.Text.Encoding]::Unicode.GetBytes(\"$Argument`n\")", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("$StartInfo.ArgumentList.Add($Argument)", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("Assert-EqualBytes $Expected $Actual", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("OK ${Rid}: UTF-16 argv round-trip", spikeWindowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("Unix RIDs include a deliberately non-UTF-8 `argv` byte round-trip; Windows RIDs include a non-ASCII UTF-16 command-line round-trip", design, StringComparison.Ordinal);
        Assert.Contains("raw non-UTF-8 Unix `argv` bytes; non-ASCII Windows UTF-16 command line", design, StringComparison.Ordinal);
        Assert.Contains("local transcript plus CI proof", design, StringComparison.Ordinal);
        Assert.Contains("Unix jobs run `spike/build-unix.sh` for `osx-arm64`, `osx-x64`, `linux-x64`, and `linux-arm64`", design, StringComparison.Ordinal);
        Assert.Contains("Windows jobs run `spike/build-windows.ps1` for `win-x64` and `win-arm64`", design, StringComparison.Ordinal);
        Assert.Contains("The complete release matrix is reproduced in CI", design, StringComparison.Ordinal);
        Assert.Contains("green CI run for the commit under test", design, StringComparison.Ordinal);
        Assert.DoesNotContain("Reproducing on the remaining four RIDs", design, StringComparison.Ordinal);
        Assert.DoesNotContain("Reproducing on the remaining five RIDs", design, StringComparison.Ordinal);
        Assert.DoesNotContain("The remaining **four** RIDs", design, StringComparison.Ordinal);
        Assert.DoesNotContain("raw non-UTF-8 `argv`/`envp` byte round-trip, built and run on all six RIDs", design, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies native binary differentials cover generated help, man, and completion artifacts.
    /// </summary>
    [Fact]
    public void NativeGeneratedArtifactDifferentialsAreWired()
    {
        string root = FindRepositoryRoot();
        string appBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-unix.sh"));
        string generatedArtifactScript = File.ReadAllText(Path.Combine(root, "native", "test-generated-artifacts-unix.sh"));
        string oracleScript = File.ReadAllText(Path.Combine(root, "eng", "read-ripgrep-oracle.sh"));

        Assert.Contains("\"$ROOT/native/test-generated-artifacts-unix.sh\" \"$RID\" \"$BIN/scout\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("read_lock_rid_table_value()", oracleScript, StringComparison.Ordinal);
        Assert.Contains("oracle_environment()", oracleScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"path\" \"ripgrep_rg_path\"", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"sha256\" \"ripgrep_rg_sha256\"", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("resolve_repo_path", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("ripgrep_rg_path", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("ripgrep_rg_sha256", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case help_long --help", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case help_short -h", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_man --generate man", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_man_inline --generate=man", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_complete_bash --generate complete-bash", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_complete_zsh --generate complete-zsh", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_complete_fish --generate complete-fish", generatedArtifactScript, StringComparison.Ordinal);
        Assert.Contains("compare_case generate_complete_powershell --generate complete-powershell", generatedArtifactScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies native release packaging is wired for every shipped RID.
    /// </summary>
    [Fact]
    public void NativeReleasePackagingIsWiredForEveryRid()
    {
        string root = FindRepositoryRoot();
        string unixBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-unix.sh"));
        string windowsBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-windows.ps1"));
        string unixPackageScript = File.ReadAllText(Path.Combine(root, "eng", "package-release.sh"));
        string windowsPackageScript = File.ReadAllText(Path.Combine(root, "eng", "package-release.ps1"));

        Assert.Contains("\"$ROOT/eng/package-release.sh\" \"$RID\"", unixBuildScript, StringComparison.Ordinal);
        Assert.Contains("eng\\package-release.ps1", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("artifacts/packages", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("artifacts\\packages", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("scout-$RID.tar.gz", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("scout-$Rid.zip", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("PARITY.md", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("PARITY.md", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("SCOUT-PACKAGE.txt", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("SCOUT-PACKAGE.txt", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("4857d6fa67db69a95cd4b6f2adda5d807d4d0119", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("4857d6fa67db69a95cd4b6f2adda5d807d4d0119", windowsPackageScript, StringComparison.Ordinal);
        Assert.Contains("sha256_file \"$ARCHIVE\"", unixPackageScript, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -Algorithm SHA256 -Path $Archive", windowsPackageScript, StringComparison.Ordinal);

        string[] unixRids = ["osx-arm64", "osx-x64", "linux-x64", "linux-arm64"];
        for (int index = 0; index < unixRids.Length; index++)
        {
            Assert.Contains(unixRids[index], unixPackageScript, StringComparison.Ordinal);
        }

        Assert.Contains("[ValidateSet(\"win-x64\", \"win-arm64\")]", windowsPackageScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the release trademark/search gate records known name collisions.
    /// </summary>
    [Fact]
    public void TrademarkCheckRecordsKnownCollisions()
    {
        string root = FindRepositoryRoot();
        string design = File.ReadAllText(Path.Combine(root, "docs", "DESIGN.md"));
        string trademarkCheck = File.ReadAllText(Path.Combine(root, "docs", "TRADEMARK-CHECK.md"));

        Assert.Contains("docs/TRADEMARK-CHECK.md", design, StringComparison.Ordinal);
        Assert.Contains("Date: 2026-06-01", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Gate status: done", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("not a legal opinion", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Scout (ripgrep port)", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("USPTO Trademark Search", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("https://tmsearch.uspto.gov/", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Docker Scout", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("docker scout", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Scout Monitoring", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Scout APM", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("openSUSE Scout", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("crates.io `scout`", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("npm `scout`", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("No searched source showed an exact conflict", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Do not claim the name is unique.", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Do not ship an `sc` alias.", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("Re-run this check before any public v1.0 announcement", trademarkCheck, StringComparison.Ordinal);
        Assert.Contains("mechanical rename path", trademarkCheck, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies generated help, man, and completion payloads are build-time source generator inputs.
    /// </summary>
    [Fact]
    public void GeneratedArtifactsAreSourceGenerated()
    {
        string root = FindRepositoryRoot();
        string helpOutput = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "HelpOutput.cs"));
        string generateOutput = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "GenerateOutput.cs"));
        string preflight = File.ReadAllText(Path.Combine(root, "eng", "preflight.sh"));
        string verifier = File.ReadAllText(Path.Combine(root, "eng", "verify-generated-artifacts.sh"));
        string projectPath = Path.Combine(root, "src", "Scout.App", "Scout.App.csproj");
        var project = XDocument.Load(projectPath);

        Assert.DoesNotContain("private const string", helpOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string", generateOutput, StringComparison.Ordinal);
        Assert.Contains("\"$ROOT/eng/verify-generated-artifacts.sh\" \"$RG_PATH\"", preflight, StringComparison.Ordinal);
        Assert.Contains("base64 -d | gzip -dc", verifier, StringComparison.Ordinal);
        Assert.Contains("cmp -s", verifier, StringComparison.Ordinal);
        Assert.Contains("diff -u", verifier, StringComparison.Ordinal);
        Assert.Contains(
            project.Descendants("CompilerVisibleItemMetadata"),
            element =>
                string.Equals((string?)element.Attribute("Include"), "AdditionalFiles", StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute("MetadataName"), "ScoutGeneratedArtifactClass", StringComparison.Ordinal));

        var expectedArtifacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeneratedArtifacts/man.base64"] = "GeneratedManPageArtifact",
            ["GeneratedArtifacts/complete-bash.base64"] = "GeneratedCompleteBashArtifact",
            ["GeneratedArtifacts/complete-zsh.base64"] = "GeneratedCompleteZshArtifact",
            ["GeneratedArtifacts/complete-fish.base64"] = "GeneratedCompleteFishArtifact",
            ["GeneratedArtifacts/complete-powershell.base64"] = "GeneratedCompletePowerShellArtifact",
            ["GeneratedArtifacts/help-short.base64"] = "GeneratedShortHelpArtifact",
            ["GeneratedArtifacts/help-long.base64"] = "GeneratedLongHelpArtifact",
        };

        var actualArtifacts = project.Descendants("AdditionalFiles")
            .Select(element => new
            {
                Include = ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'),
                ClassName = (string?)element.Attribute("ScoutGeneratedArtifactClass") ?? string.Empty,
            })
            .Where(entry => entry.Include.StartsWith("GeneratedArtifacts/", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Include, entry => entry.ClassName, StringComparer.Ordinal);

        Assert.Equal(expectedArtifacts, actualArtifacts);

        Assert.Contains("compare_artifact help_short help-short.base64 -h", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact help_long help-long.base64 --help", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact generate_man man.base64 --generate man", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact generate_complete_bash complete-bash.base64 --generate complete-bash", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact generate_complete_zsh complete-zsh.base64 --generate complete-zsh", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact generate_complete_fish complete-fish.base64 --generate complete-fish", verifier, StringComparison.Ordinal);
        Assert.Contains("compare_artifact generate_complete_powershell complete-powershell.base64 --generate complete-powershell", verifier, StringComparison.Ordinal);
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
    /// Verifies vendored regex-syntax Unicode tables match the pinned Unicode version.
    /// </summary>
    [Fact]
    public void VendoredUnicodeTablesMatchPinnedRegexSyntaxVersion()
    {
        string root = FindRepositoryRoot();
        string unicodeVersion = File.ReadAllText(Path.Combine(root, "upstream", "UNICODE-VERSION")).Trim();
        string preflight = File.ReadAllText(Path.Combine(root, "eng", "preflight.sh"));
        string verifier = File.ReadAllText(Path.Combine(root, "eng", "verify-unicode-data.sh"));
        string ucdReadme = File.ReadAllText(Path.Combine(root, "upstream", "ucd", "README.md"));
        string regexByteClass = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata", "RegexByteClass.cs"));
        string regexSyntaxParseState = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata.Syntax", "RegexSyntaxParseState.cs"));
        string regexUnicodePropertyKind = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata.Syntax", "RegexUnicodePropertyKind.cs"));
        string regexUnicodePropertyNames = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata.Syntax", "RegexUnicodePropertyNames.cs"));
        string regexUnicodeTables = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata", "RegexUnicodeTables.cs"));
        string ucdArchive = Path.Combine(root, "upstream", "ucd", "UCD-16.0.0.zip");
        string tablesRoot = Path.Combine(root, "upstream", "regex-syntax-0.8.8", "unicode_tables");
        string[] expectedTables =
        [
            "age.rs",
            "case_folding_simple.rs",
            "general_category.rs",
            "grapheme_cluster_break.rs",
            "perl_decimal.rs",
            "perl_space.rs",
            "perl_word.rs",
            "property_bool.rs",
            "property_names.rs",
            "property_values.rs",
            "script.rs",
            "script_extension.rs",
            "sentence_break.rs",
            "word_break.rs",
        ];

        Assert.Equal("16.0.0", unicodeVersion);
        Assert.True(File.Exists(ucdArchive), "Missing vendored UCD archive.");
        Assert.Contains("\"$ROOT/eng/verify-unicode-data.sh\"", preflight, StringComparison.Ordinal);
        Assert.Contains("source_url = \"https://www.unicode.org/Public/16.0.0/ucd/UCD.zip\"", ucdReadme, StringComparison.Ordinal);
        Assert.Contains("sha256 = \"c86dd81f2b14a43b0cc064aa5f89aa7241386801e35c59c7984e579832634eb2\"", ucdReadme, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_SHA256=\"c86dd81f2b14a43b0cc064aa5f89aa7241386801e35c59c7984e579832634eb2\"", verifier, StringComparison.Ordinal);
        Assert.Contains("unzip -l \"$ARCHIVE\"", verifier, StringComparison.Ordinal);
        Assert.Contains("eng/generate-regex-unicode-tables.py", verifier, StringComparison.Ordinal);
        Assert.Contains("cmp \"$generated\" \"$ROOT/src/Scout.Automata/RegexUnicodeTables.cs\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"UnicodeData.txt\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"CaseFolding.txt\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"PropertyAliases.txt\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"PropertyValueAliases.txt\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"Scripts.txt\"", verifier, StringComparison.Ordinal);
        Assert.Contains("require_archive_entry \"ScriptExtensions.txt\"", verifier, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(tablesRoot, "LICENSE-UNICODE")), "Missing vendored Unicode license.");
        Assert.True(File.Exists(Path.Combine(tablesRoot, "mod.rs")), "Missing regex-syntax unicode_tables module file.");
        Assert.Contains("Unicode version: " + unicodeVersion + ".", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("internal static bool IsGeneralCategory(RegexUnicodePropertyKind kind, Rune value)", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("internal static bool IsBooleanProperty(RegexUnicodePropertyKind kind, Rune value)", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("internal static bool IsBreakProperty(RegexUnicodePropertyKind kind, Rune value)", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("internal static bool IsSimpleCaseFold(Rune left, Rune right)", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] DecimalNumber", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] PerlWord", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] PerlSpace", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] Alphabetic", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarPair[] SimpleCaseFold", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BreakPropertyGraphemeClusterBreakRegionalIndicator", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BreakPropertyWordBreakHebrewLetter", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BreakPropertySentenceBreakSContinue", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BooleanPropertyMath", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BooleanPropertyEmoji", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] BooleanPropertyExtendedPictographic", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("private static readonly UnicodeScalarRange[] GeneralCategoryUppercaseLetter", regexUnicodeTables, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsDecimalNumber", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsPerlWord", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsPerlSpace", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsAlphabetic", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsGeneralCategory", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsBooleanProperty", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsBreakProperty", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodeTables.IsSimpleCaseFold", regexByteClass, StringComparison.Ordinal);
        Assert.Contains("TryParseUnicodePropertyClass", regexSyntaxParseState, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetKind", regexUnicodePropertyNames, StringComparison.Ordinal);
        Assert.Contains("public static bool NameEquals", regexUnicodePropertyNames, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodePropertyNames.TryGetKind", regexSyntaxParseState, StringComparison.Ordinal);
        Assert.Contains("RegexUnicodePropertyNames.TryGetKind", regexByteClass, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseUnicodeAnyClass", regexSyntaxParseState, StringComparison.Ordinal);
        Assert.Contains("UppercaseLetter", regexUnicodePropertyKind, StringComparison.Ordinal);
        Assert.Contains("ExtendedPictographic", regexUnicodePropertyKind, StringComparison.Ordinal);
        Assert.DoesNotContain("Rune.GetUnicodeCategory", regexByteClass, StringComparison.Ordinal);
        Assert.DoesNotContain("Rune.IsWhiteSpace", regexByteClass, StringComparison.Ordinal);
        string[] actualTables = Directory.GetFiles(tablesRoot, "*.rs")
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "mod.rs", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(expectedTables, actualTables);

        for (int index = 0; index < expectedTables.Length; index++)
        {
            string table = File.ReadAllText(Path.Combine(tablesRoot, expectedTables[index]));
            Assert.Contains("DO NOT EDIT THIS FILE. IT WAS AUTOMATICALLY GENERATED BY:", table, StringComparison.Ordinal);
            Assert.Contains("ucd-" + unicodeVersion, table, StringComparison.Ordinal);
            Assert.Contains("Unicode version: " + unicodeVersion + ".", table, StringComparison.Ordinal);
            Assert.Contains("ucd-generate 0.3.1", table, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies every ported or behavior-replaced upstream project records its pinned provenance.
    /// </summary>
    [Fact]
    public void PortedProjectsRecordUpstreamProvenance()
    {
        string root = FindRepositoryRoot();
        (string RelativePath, string[] Fragments)[] upstreamFiles =
        [
            ("src/Scout.App/UPSTREAM.md",
            [
                "name = \"ripgrep\"",
                "version = \"15.1.0\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"grep\"",
                "version = \"0.4.1\"",
                "disposition = \"workspace facade folded into Scout project references\"",
                "name = \"lexopt\"",
                "version = \"0.3.1\"",
                "name = \"textwrap\"",
                "version = \"0.16.2\"",
            ]),
            ("src/Scout.Automata/UPSTREAM.md",
            [
                "name = \"regex-automata\"",
                "version = \"0.4.13\"",
                "checksum = \"5276caf25ac86c8d810222b3dbb938e512c55c6831a10f3e6ed1c93b84041f1c\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"regex\"",
                "version = \"1.12.2\"",
            ]),
            ("src/Scout.Automata.AhoCorasick/UPSTREAM.md",
            [
                "name = \"aho-corasick\"",
                "version = \"1.1.3\"",
                "checksum = \"8e60d3430d3a69478ad0993f19238d2df97c507009a52b3c10addcd7f6bcb916\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Automata.Memmem/UPSTREAM.md",
            [
                "name = \"memchr\"",
                "version = \"2.7.6\"",
                "checksum = \"f52b00d39961fc5b2736ea853c9cc86238e165017a493d1d5c8eac6bdc4cc273\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Automata.Syntax/UPSTREAM.md",
            [
                "name = \"regex-syntax\"",
                "version = \"0.8.8\"",
                "checksum = \"7a2d987857b319362043e95f5353c0535c1f58eec5336fdfcf626430af7def58\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Bytes/UPSTREAM.md",
            [
                "name = \"bstr\"",
                "version = \"1.12.0\"",
                "checksum = \"234113d19d0d7d613b40e86fb654acf958910802bcceab913a4f9e7cda03b1a4\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Cli/UPSTREAM.md",
            [
                "name = \"grep-cli\"",
                "version = \"0.1.12\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"termcolor\"",
                "version = \"1.4.1\"",
                "name = \"winapi-util\"",
                "version = \"0.1.11\"",
            ]),
            ("src/Scout.Diagnostics/UPSTREAM.md",
            [
                "name = \"log\"",
                "version = \"0.4.28\"",
                "checksum = \"34080505efa8e45a4b816c349525ebe327ceaa8559756f0356cba97ef3bf7432\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Encoding/UPSTREAM.md",
            [
                "name = \"encoding_rs\"",
                "version = \"0.8.35\"",
                "checksum = \"75030f3c4f45dafd7586dd6780965a8c7e8e285a5ecb86713e63a79c5b2766f3\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Encoding.Io/UPSTREAM.md",
            [
                "name = \"encoding_rs_io\"",
                "version = \"0.1.7\"",
                "checksum = \"1cc3c5651fb62ab8aa3103998dade57efdd028544bd300516baa31840c252a83\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Errors/UPSTREAM.md",
            [
                "name = \"anyhow\"",
                "version = \"1.0.100\"",
                "checksum = \"a23eb6b1614318a8071c9b2521f36b424b2c83db5eb3a0fead4a6c0809af6e61\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Globbing/UPSTREAM.md",
            [
                "name = \"globset\"",
                "version = \"0.4.18\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Ignore/UPSTREAM.md",
            [
                "name = \"ignore\"",
                "version = \"0.4.25\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"walkdir\"",
                "version = \"2.5.0\"",
                "checksum = \"29790946404f91d9c5d06f9874efddea1dc06c5efe94541a7d6863108e3a5e4b\"",
                "name = \"same-file\"",
                "version = \"1.0.6\"",
                "checksum = \"93fc1dc3aaa9bfed95e02e6eadabb4baf7e3078b0bd1b4d7b6b0b68378900502\"",
                "name = \"crossbeam-deque\"",
                "version = \"0.8.6\"",
                "checksum = \"9dd111b7b7f7d55b72c0a6ae361660ee5853c9af73f70c3c2ef6858b950e2e51\"",
            ]),
            ("src/Scout.Matching/UPSTREAM.md",
            [
                "name = \"grep-matcher\"",
                "version = \"0.1.8\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Os/UPSTREAM.md",
            [
                "name = \"libc\"",
                "version = \"0.2.177\"",
                "name = \"windows-sys\"",
                "version = \"0.61.2\"",
                "name = \"winapi-util\"",
                "version = \"0.1.11\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Pcre2/UPSTREAM.md",
            [
                "name = \"grep-pcre2\"",
                "version = \"0.1.9\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"pcre2\"",
                "version = \"0.2.11\"",
                "name = \"pcre2-sys\"",
                "version = \"0.2.10\"",
                "native/pcre2/UPSTREAM",
            ]),
            ("src/Scout.Printing/UPSTREAM.md",
            [
                "name = \"grep-printer\"",
                "version = \"0.3.1\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"serde_json\"",
                "version = \"1.0.145\"",
                "name = \"itoa\"",
                "version = \"1.0.15\"",
                "name = \"ryu\"",
                "version = \"1.0.20\"",
            ]),
            ("src/Scout.Regex/UPSTREAM.md",
            [
                "name = \"grep-regex\"",
                "version = \"0.1.14\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
            ]),
            ("src/Scout.Searching/UPSTREAM.md",
            [
                "name = \"grep-searcher\"",
                "version = \"0.1.16\"",
                "commit = \"" + PinnedRipgrepCommit + "\"",
                "name = \"memmap2\"",
                "version = \"0.9.9\"",
                "checksum = \"744133e4a0e0a658e1374cf3bf8e415c4052a15a111acd372764c55b4177d490\"",
            ]),
        ];

        for (int fileIndex = 0; fileIndex < upstreamFiles.Length; fileIndex++)
        {
            (string relativePath, string[] fragments) = upstreamFiles[fileIndex];
            string path = Path.Combine(root, relativePath);

            Assert.True(File.Exists(path), "Missing upstream provenance file: " + relativePath);
            string text = File.ReadAllText(path);
            for (int fragmentIndex = 0; fragmentIndex < fragments.Length; fragmentIndex++)
            {
                Assert.Contains(fragments[fragmentIndex], text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Verifies every Cargo.lock package has an explicit Scout disposition.
    /// </summary>
    [Fact]
    public void CargoLockPackagesHaveExplicitDisposition()
    {
        string root = FindRepositoryRoot();
        string cargoLock = File.ReadAllText(Path.Combine(root, "upstream", "Cargo.lock"));
        string provenance = File.ReadAllText(Path.Combine(root, "docs", "UPSTREAM-SYNC.md"));
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "UPSTREAM.md", SearchOption.AllDirectories))
        {
            provenance += "\n" + File.ReadAllText(path);
        }

        var missing = new List<string>();
        foreach (Match match in CargoLockPackagePattern().Matches(cargoLock))
        {
            string name = match.Groups["name"].Value;
            string version = match.Groups["version"].Value;
            bool hasName = provenance.Contains("name = \"" + name + "\"", StringComparison.Ordinal) ||
                provenance.Contains("`" + name + "`", StringComparison.Ordinal);
            bool hasVersion = provenance.Contains("version = \"" + version + "\"", StringComparison.Ordinal) ||
                provenance.Contains("`" + version + "`", StringComparison.Ordinal);

            if (!hasName || !hasVersion)
            {
                missing.Add(name + " " + version);
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Verifies lockfile entries without shipped Scout behavior are explicitly dispositioned.
    /// </summary>
    [Fact]
    public void UpstreamSyncDocumentsNoSurfaceLockfileEntries()
    {
        string root = FindRepositoryRoot();
        string policy = File.ReadAllText(Path.Combine(root, "docs", "UPSTREAM-SYNC.md"));
        string[] requiredFragments =
        [
            "## Lockfile Entries With No Scout Port",
            "| `arbitrary`, `derive_arbitrary` | `1.4.2` |",
            "| `cc`, `find-msvc-tools`, `jobserver`, `pkg-config`, `shlex` | `1.2.41`, `0.1.4`, `0.1.34`, `0.3.32`, `1.3.0` |",
            "| `cfg-if` | `1.0.4` |",
            "| `crossbeam-channel`, `crossbeam-epoch`, `crossbeam-utils` | `0.5.15`, `0.9.18`, `0.8.21` |",
            "| `getrandom`, `r-efi`, `wasip2`, `wit-bindgen` | `0.3.4`, `5.3.0`, `1.0.1+wasi-0.2.4`, `0.46.0` |",
            "| `glob` | `0.3.3` |",
            "| `proc-macro2`, `quote`, `syn`, `unicode-ident` | `1.0.101`, `1.0.41`, `2.0.107`, `1.0.20` |",
            "| `serde`, `serde_core`, `serde_derive` | `1.0.228` |",
            "| `tikv-jemallocator`, `tikv-jemalloc-sys` | `0.6.1`, `0.6.1+5.3.0-1-ge13ca993e8ccb9ba9847cc330696e02839f328f7` |",
            "| `windows-link` | `0.2.1` |",
        ];

        for (int index = 0; index < requiredFragments.Length; index++)
        {
            Assert.Contains(requiredFragments[index], policy, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies third-party notices include the license texts required by the design.
    /// </summary>
    [Fact]
    public void ThirdPartyNoticesReproduceRequiredLicenses()
    {
        string root = FindRepositoryRoot();
        string notices = File.ReadAllText(Path.Combine(root, "docs", "THIRD-PARTY-NOTICES.md"));

        Assert.DoesNotContain("release blocker", notices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("| ripgrep |", notices, StringComparison.Ordinal);
        Assert.Contains("| regex-syntax | 0.8.8 | MIT OR Apache-2.0 |", notices, StringComparison.Ordinal);
        Assert.Contains("| encoding_rs | 0.8.35 | MIT OR Apache-2.0; WHATWG data under BSD-3-Clause |", notices, StringComparison.Ordinal);
        Assert.Contains("| crossbeam-deque | 0.8.6 | MIT OR Apache-2.0 |", notices, StringComparison.Ordinal);
        Assert.Contains("| PCRE2 C library | 10.46", notices, StringComparison.Ordinal);
        Assert.Contains("Apache License\n                        Version 2.0, January 2004", notices, StringComparison.Ordinal);
        Assert.Contains("This is free and unencumbered software released into the public domain.", notices, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2019 The Crossbeam Project Developers", notices, StringComparison.Ordinal);
        Assert.Contains("Copyright © WHATWG (Apple, Google, Mozilla, Microsoft).", notices, StringComparison.Ordinal);
        Assert.Contains("Original API code Copyright (c) 1997-2012 University of Cambridge", notices, StringComparison.Ordinal);
        Assert.Contains("New API code Copyright (c) 2016-2024 University of Cambridge", notices, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies PCRE2 provenance is recorded and matches the vendored <c>pcre2-sys</c> header.
    /// </summary>
    [Fact]
    public void Pcre2UpstreamPinMatchesVendoredHeader()
    {
        string root = FindRepositoryRoot();
        string directoryBuildProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        string upstream = File.ReadAllText(Path.Combine(root, "native", "pcre2", "UPSTREAM"));
        string sourceRoot = Path.Combine(root, "native", "pcre2", "pcre2-10.46");
        string buildScript = File.ReadAllText(Path.Combine(root, "native", "pcre2", "build-unix.sh"));
        string windowsBuildScript = File.ReadAllText(Path.Combine(root, "native", "pcre2", "build-windows.ps1"));
        string appBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-unix.sh"));
        string windowsAppBuildScript = File.ReadAllText(Path.Combine(root, "native", "build-app-windows.ps1"));
        string differentialScript = File.ReadAllText(Path.Combine(root, "native", "test-pcre2-differential-unix.sh"));
        string invalidUtf8DifferentialScript = File.ReadAllText(Path.Combine(root, "native", "test-invalid-utf8-differential-unix.sh"));
        string pcre2Library = File.ReadAllText(Path.Combine(root, "src", "Scout.Pcre2", "Pcre2Library.cs"));
        string pcre2Regex = File.ReadAllText(Path.Combine(root, "src", "Scout.Pcre2", "Pcre2Regex.cs"));
        string pcre2SearchOperations = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "Pcre2SearchOperations.cs"));
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string headerPath = Path.Combine(sourceRoot, "include", "pcre2.h");
        string pinnedPcre2RipgrepBinaryPath = PinnedPcre2RipgrepOracle.ExecutablePath;
        string defaultPcre2RipgrepPath = PinnedRipgrepOracle.ReadHostOracleValue("pcre2_path", "ripgrep_pcre2_rg_path");
        string expectedPcre2RipgrepSha256 = PinnedPcre2RipgrepOracle.ExpectedSha256;
        string expectedPcre2ReportedVersion = PinnedPcre2RipgrepOracle.ReportedVersion;

        Assert.True(File.Exists(headerPath), "Missing vendored pcre2.h: " + headerPath);
        Assert.True(File.Exists(pinnedPcre2RipgrepBinaryPath), "Missing pinned PCRE2 ripgrep binary: " + pinnedPcre2RipgrepBinaryPath);
        PinnedPcre2RipgrepOracle.VerifyHash();
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
        Assert.Contains("pcre2_path = \"" + defaultPcre2RipgrepPath + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("pcre2_sha256 = \"" + expectedPcre2RipgrepSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
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
        Assert.Contains("[ValidateSet(\"win-x64\", \"win-arm64\")]", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("cl.exe", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("lib.exe", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2-8.lib", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/DPCRE2_CODE_UNIT_WIDTH=8", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/DPCRE2_STATIC=1", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/DSUPPORT_PCRE2_8=1", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/DSUPPORT_UNICODE=1", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("/DSUPPORT_JIT=1", windowsBuildScript, StringComparison.Ordinal);
        Assert.Contains("osx-arm64|osx-x64|linux-x64|linux-arm64", appBuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("not implemented", appBuildScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"$ROOT/native/pcre2/build-unix.sh\" \"$RID\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("REAL_BIN=\"$BIN/scout-real\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-DSCOUT_LAUNCHER", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("artifacts/native/pcre2/$RID/lib/libpcre2-8.a", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-Wl,-force_load,\"$PCRE2_LIB\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-Wl,--whole-archive \"$PCRE2_LIB\" -Wl,--no-whole-archive", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-Wl,--start-group", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("libSystem.Security.Cryptography.Native.OpenSsl.a", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-lstdc++ -lz -lpthread -ldl -lm -lrt", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("PCRE2_SYMBOL_PREFIX=_", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2_config_8", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2_compile_8", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2_match_8", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2_match_data_create_from_pattern_8", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --json 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --json -o 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -o '.*o(?!.*\\s)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --count 'o(?=o)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --count-matches 'o(?=o)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("(cd \"$BIN/explicit-cwd\" && \"$BIN/scout\" needle .", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --files-with-matches 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --files-without-match 'nomatch(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -n 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -n -C1 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -n --passthru 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -n -o -C1 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -n -r X -C1 'foo(?=bar)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2-smoke-2.txt", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("PCRE2 multi-file prefixes", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("PCRE2 context replacement", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("PCRE2 explicit stdin", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --column 'bar'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P -H -n --column -b -o 'o(?=o)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --multiline '(?s)Start(?=.*thing2)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --json --multiline '(?s)Start(?=.*thing2)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --multiline --files-with-matches '(?s)Start(?=.*thing2)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --multiline --count '(?s)def (\\w+);(?=.*use \\w+)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("-P --multiline --count-matches '(?s)def (\\w+);(?=.*use \\w+)'", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("DIFFERENTIAL_MODE=\"${2:---with-differentials}\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("--smoke-only", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("if [ \"$DIFFERENTIAL_MODE\" = \"--with-differentials\" ]; then", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet(\"win-x64\", \"win-arm64\")]", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("native\\pcre2\\build-windows.ps1", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("scout_wmain.c", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("cl.exe", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("link.exe", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("/ENTRY:wmainCRTStartup", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("bootstrapperdll.obj", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("Runtime.WorkstationGC.lib", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("System" + ".Globalization.Native.Aot.lib", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("System" + ".IO.Compression.Native.Aot.lib", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("/WHOLEARCHIVE:$Pcre2Lib", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("pcre2-8.lib", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("& $OutputExe -V", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("--pcre2-version", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("$SmokePath2", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("Unexpected PCRE2 multi-file output", windowsAppBuildScript, StringComparison.Ordinal);
        Assert.Contains("\"$ROOT/native/test-pcre2-differential-unix.sh\" \"$RID\" \"$BIN/scout\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"pcre2_path\" \"ripgrep_pcre2_rg_path\"", differentialScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"pcre2_sha256\" \"ripgrep_pcre2_rg_sha256\"", differentialScript, StringComparison.Ordinal);
        Assert.Contains("pcre2_path = \"" + defaultPcre2RipgrepPath + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("pcre2_sha256 = \"" + expectedPcre2RipgrepSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("ripgrep_pcre2_rg_profile = \"release-lto\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("ripgrep_pcre2_rg_features = \"pcre2\"", prerequisiteLock, StringComparison.Ordinal);
        if (string.Equals(PinnedRipgrepOracle.HostOracleEnvironment, "local", StringComparison.Ordinal))
        {
            Assert.Contains("ripgrep_pcre2_rg_path = \"" + defaultPcre2RipgrepPath + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("ripgrep_pcre2_rg_sha256 = \"" + expectedPcre2RipgrepSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
        }

        Assert.Contains("ripgrep_pcre2_reported_version = \"" + expectedPcre2ReportedVersion + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("compare_case f1155_auto_hybrid_regex exact --no-pcre2 --auto-hybrid-regex '(?<=the )Sherlock'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("run_tool_stdin()", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case fixed_literal exact -P -F -n 'foo(?=bar)' pcre2-fixed", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case fixed_literal_json mask-elapsed -P -F --json 'foo(?=bar)' pcre2-fixed", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case fixed_literal_multiline exact -P -F --multiline -n 'foo\\nbar' pcre2-fixed", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_line exact -P --null-data -n 'needle' pcre2-null", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_only_matching exact -P --null-data -o 'needle' pcre2-null", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_count exact -P --null-data --count 'needle' pcre2-null", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_json mask-elapsed -P --null-data --json 'needle' pcre2-null", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_context exact -P --null-data -n -C1 'needle' pcre2-null", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_multiline exact -P --null-data --multiline -n '(?s)foo.*bar' pcre2-null-multiline", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_multiline_only_matching exact -P --null-data --multiline -o '(?s)foo.*bar' pcre2-null-multiline", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_multiline_count exact -P --null-data --multiline --count '(?s)foo.*bar' pcre2-null-multiline", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_multiline_json mask-elapsed -P --null-data --json --multiline '(?s)foo.*bar' pcre2-null-multiline", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case null_data_multiline_context exact -P --null-data --multiline -n -C1 '(?s)foo.*bar' pcre2-null-multiline", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case basic_lookahead_multi_file sort-lines -P -n 'foo(?=bar)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case recursive_lookahead sort-lines -P -n 'foo(?=bar)' pcre2-dir", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case recursive_lookahead_threads sort-lines -P --threads 4 -n 'foo(?=bar)' pcre2-dir", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case line_regexp exact -P -x 'foo(?=bar)bar' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case word_regexp exact -P -w 'foo(?=-)' pcre2-word.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case context exact -P -n -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case passthru exact -P -n --passthru 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case context_only_matching exact -P -n -o -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case context_replacement exact -P -n -r X -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case only_matching_replacement exact -P -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case only_matching_replacement_columns exact -P -n --column -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_lookahead exact -P --vimgrep 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_only_matching exact -P --vimgrep -o 'foo(?=bar)|foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_replacement exact -P --vimgrep -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_only_matching_replacement exact -P --vimgrep -o -r X 'foo(?=bar)|foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_context exact -P --vimgrep -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_context_replacement exact -P --vimgrep -r X -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_invert exact -P --vimgrep -v 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_count exact -P --vimgrep --count 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case count_replacement exact -P --count -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case count_matches_replacement exact -P --count-matches -r X 'foo(?=bar)|foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case files_with_matches_replacement exact -P --files-with-matches -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case files_without_match_replacement exact -P --files-without-match -r X 'notfound' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case count_context_replacement exact -P --count -C1 -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_stdin_case vimgrep_implicit_stdin exact \"$WORK/pcre2-smoke.txt\" -P --vimgrep 'foo(?=bar)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_stdin_case vimgrep_count_stdin exact \"$WORK/pcre2-smoke.txt\" -P --vimgrep --count 'foo(?=bar)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_stdin_case explicit_stdin_lookahead exact \"$WORK/pcre2-smoke.txt\" -P 'foo(?=bar)' -", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_stdin_case implicit_stdin_lookahead exact \"$WORK/pcre2-smoke.txt\" -P 'foo(?=bar)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("mask-elapsed-sort-lines", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_lookahead_only_matching mask-elapsed -P --json -o 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multi_file mask-elapsed-sort-lines -P --json 'foo(?=bar)' pcre2-smoke.txt pcre2-smoke-2.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_quiet mask-elapsed -P --json -q 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("seconds spent searching", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_stats_lookahead mask-elapsed -P --json --stats 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_replacement mask-elapsed -P --json -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_only_matching_replacement mask-elapsed -P --json -o -r X 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_context_lookahead mask-elapsed -P --json -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_context_replacement mask-elapsed -P --json -r X -C1 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_passthru_lookahead mask-elapsed -P --json --passthru 'foo(?=bar)' pcre2-context", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multiline_replacement mask-elapsed -P --json --multiline -r X '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multiline_context mask-elapsed -P --json --multiline -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multiline_context_replacement mask-elapsed -P --json --multiline -r X -C1 '(?s)foo\\nbar|foo' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multiline_passthru_max_count mask-elapsed -P --json --multiline --passthru -m1 '(?s)foo\\nbar|foo' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_multiline_invert_context mask-elapsed -P --json --multiline -v -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case stats_lookahead mask-elapsed -P --stats 'foo(?=bar)' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case stats_recursive_lookahead_threads mask-elapsed-sort-lines -P --threads 4 --stats 'foo(?=bar)' pcre2-dir", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case r1401_lookahead_only_matching_1 exact -P -N -o '.*o(?!.*\\s)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case json_lookbehind mask-elapsed -P -U --json '(?<=foo\\n)bar'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case r1412_lookbehind_replacement exact -P -nU -rquux '(?<=foo\\n)bar'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case r1573_count exact -P --multiline --count '(?s)def (\\w+);(?=.*use \\w+)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_count_only_matching exact -P --multiline -o --count 'foo' pcre2-smoke.txt", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case r3139_multiline_files_with_matches exact -P --multiline --files-with-matches '(?s)Start(?=.*thing2)'", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_only_matching exact -P --multiline -o '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_only_matching_replacement exact -P --multiline -o -r X '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context exact -P --multiline -n -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_before_after exact -P --multiline -n -B1 -A2 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_passthru exact -P --multiline -n --passthru '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_passthru_max_count exact -P --multiline -n --passthru -m1 '(?s)foo\\nbar|foo' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context_max_count exact -P --multiline -n -C1 -m1 '(?s)foo\\nbar|foo' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context_only_matching exact -P --multiline -n -o -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context_only_matching_replacement exact -P --multiline -n -o -r X -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context_replacement exact -P --multiline -n -r X -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_context_replacement_max_count exact -P --multiline -n -r X -C1 -m1 '(?s)foo\\nbar|foo' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline exact -P --vimgrep --multiline '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_byte_offset exact -P --vimgrep -b --multiline '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_only_matching exact -P --vimgrep --multiline -o '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_replacement exact -P --vimgrep --multiline -r X '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_context exact -P --vimgrep --multiline -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_context_only_matching exact -P --vimgrep --multiline -o -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case vimgrep_multiline_context_replacement exact -P --vimgrep --multiline -r X -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert exact -P --multiline -n -v '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_context exact -P --multiline -n -v -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_passthru exact -P --multiline -n -v --passthru '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_count exact -P --multiline -v --count '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_count_matches exact -P --multiline -v --count-matches '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_files_with_matches exact -P --multiline -v --files-with-matches '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_files_without_match exact -P --multiline -v --files-without-match '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_vimgrep exact -P --vimgrep --multiline -v '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_vimgrep_context exact -P --vimgrep --multiline -v -C1 '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("compare_case multiline_invert_quiet exact -P --multiline -q -v '(?s)foo\\nbar' pcre2-multiline-vimgrep", differentialScript, StringComparison.Ordinal);
        Assert.Contains("\"$ROOT/native/test-invalid-utf8-differential-unix.sh\" \"$RID\" \"$BIN/scout\"", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"path\" \"ripgrep_rg_path\"", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"sha256\" \"ripgrep_rg_sha256\"", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("r210_explicit_invalid_utf8_path", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("json_explicit_invalid_utf8_path", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("json_recursive_invalid_utf8_path", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("invalid_utf8_pattern_argv", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("invalid_utf8_regexp_argv", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("errno.EILSEQ", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("subprocess.run", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.DoesNotContain("SKIP invalid UTF-8", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.DoesNotContain("sys.exit(0)", invalidUtf8DifferentialScript, StringComparison.Ordinal);
        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", directoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<DirectPInvoke Include=\"__Internal\" />", directoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("PCRE2 10.46 is available (JIT is available)", appBuildScript, StringComparison.Ordinal);
        Assert.Contains("ConfigVersion = 11", pcre2Library, StringComparison.Ordinal);
        Assert.Contains("SearchPcre2DirectoryParallel", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.Contains("ThreadLocal<Pcre2Regex>", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.Contains("SearchWalkPlanning.GetSearchWalkThreadCount", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.Contains("BuildParallel().Run", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.Contains("CollectPcre2SearchStats", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.DoesNotContain("!lowArgs.Stats", pcre2SearchOperations, StringComparison.Ordinal);
        Assert.Contains("Pcre2Config(ConfigVersion", pcre2Library, StringComparison.Ordinal);
        Assert.DoesNotContain("PCRE2 10.46 is available", pcre2Library, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"__Internal\", EntryPoint = \"pcre2_config_8\")]", pcre2Library, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"__Internal\", EntryPoint = \"pcre2_compile_8\")]", pcre2Regex, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"__Internal\", EntryPoint = \"pcre2_match_8\")]", pcre2Regex, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the macOS decompression tools in the prerequisite lock match the host binaries.
    /// </summary>
    [Fact]
    public void MacosDecompressionToolsMatchPinnedHashes()
    {
        (string Name, string Version, string Path, string LocalSha256, string? HostedSha256)[] tools =
        [
            ("gzip", "Apple gzip 475", "/usr/bin/gzip", "A1983798AB66B3431190813540CB0EC691DCB8EE28DE36744B88FD8B91CD9FCD", "7BD218BC6B12FCED475163901547A796736F72F99533CBEC60EEA150ED21AFA3"),
            ("bzip2", "1.0.8", "/usr/bin/bzip2", "8DA4D460440E876D81875D814F3A0EEAD38BA0FB94FEF81A9BE87560A897DEE1", "14E28B6B7955CBD6CD2A8139CA41186A922143A4FA3715DDD8E331F41DB8FC80"),
            ("xz", "5.8.2", "/opt/homebrew/bin/xz", "B7926EA19ABF39913EE064329261D03EC66271CF5EE4759E5A1A928A3E165540", "995C8E2F72446F0D0E3A29F6C3D52286CFECEDFC4FFB2B42D25C3CE1AD77034C"),
            ("zstd", "1.5.7", "/opt/homebrew/bin/zstd", "AFF8169FB421BB925FB16C44A7E0143FA2C7A941DC45CCE76B15062A2CE54917", null),
            ("lz4", "1.10.0", "/opt/homebrew/bin/lz4", "B7DCCDC84A76F0359C26C67393A6D50B4B073F8BF85078DCA7CCF877502B00E5", null),
            ("brotli", "1.2.0", "/opt/homebrew/bin/brotli", "528B0B00C1B2F8323E6185DC40D10F0324D21F9CBCCA6D8B549F6B2E49520ECF", null),
            ("uncompress", "Apple compress file_cmds-475", "/usr/bin/uncompress", "C2E461B27668BD63C4CBD85649F7C4CEB63FC2447BF657D231E0D9FD4F42A055", "AEC4BECD30850078AA28747CAA0C76227C9E848378377E37F98D531203FE6AA4"),
        ];

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        bool hosted = string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
        for (int index = 0; index < tools.Length; index++)
        {
            (string name, string version, string path, string localSha256, string? hostedSha256) = tools[index];
            string expectedSha256 = hosted && hostedSha256 is not null ? hostedSha256 : localSha256;
            Assert.Contains("name = \"" + name + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("version = \"" + version + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("path = \"" + path + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("sha256 = \"" + localSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);
            if (hostedSha256 is not null)
            {
                Assert.Contains("sha256 = \"" + hostedSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);
            }

            if (OperatingSystem.IsMacOS())
            {
                Assert.True(File.Exists(path), "Missing macOS prerequisite tool: " + path);
                byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
                Assert.Equal(expectedSha256, Convert.ToHexString(hash));
            }
        }
    }

    /// <summary>
    /// Verifies hosted macOS release gates use GitHub-hosted tool hashes instead of local machine hashes.
    /// </summary>
    [Fact]
    public void HostedMacosDecompressionToolHashesArePinned()
    {
        (string Name, string Version, string Path, string Sha256)[] tools =
        [
            ("gzip", "Apple gzip 475", "/usr/bin/gzip", "7bd218bc6b12fced475163901547a796736f72f99533cbec60eea150ed21afa3"),
            ("bzip2", "1.0.8", "/usr/bin/bzip2", "14e28b6b7955cbd6cd2a8139ca41186a922143a4fa3715ddd8e331f41db8fc80"),
            ("xz", "5.8.2", "/opt/homebrew/bin/xz", "995c8e2f72446f0d0e3a29f6c3d52286cfecedfc4ffb2b42d25c3ce1ad77034c"),
            ("uncompress", "Apple compress file_cmds-475", "/usr/bin/uncompress", "aec4becd30850078aa28747caa0c76227c9e848378377e37f98d531203fe6aa4"),
        ];

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string preflight = File.ReadAllText(Path.Combine(root, "eng", "preflight.sh"));

        Assert.Contains("read_lock_environment_table_value()", preflight, StringComparison.Ordinal);
        Assert.Contains("read_lock_environment_table_value \"tool.macos\" \"$name\" \"$HOST_ORACLE_ENVIRONMENT\" \"path\"", preflight, StringComparison.Ordinal);
        Assert.Contains("environment = \"%s\"", preflight, StringComparison.Ordinal);

        for (int index = 0; index < tools.Length; index++)
        {
            (string name, string version, string path, string sha256) = tools[index];
            string block = string.Join(
                "\n",
                "[[tool.macos]]",
                "name = \"" + name + "\"",
                "environment = \"github-actions\"",
                "version = \"" + version + "\"",
                "path = \"" + path + "\"",
                "sha256 = \"" + sha256 + "\"");
            Assert.Contains(block, prerequisiteLock, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies every pinned Linux command-line prerequisite is represented in the prerequisite lock.
    /// </summary>
    [Fact]
    public void LinuxPrerequisiteToolsCoverPinnedCommandRequirements()
    {
        (string Rid, string Name, string Package, string Binary, string Path, string Version, string Sha256)[] tools =
        [
            ("linux-x64", "gzip", "gzip", "gzip", "/usr/bin/gzip", "1.12-1", "953d326212574b5ad3cbe5f87034b0c142b6e6d71bb619c51eaa3d2ce47f7e24"),
            ("linux-x64", "bzip2", "bzip2", "bzip2", "/usr/bin/bzip2", "1.0.8-5+b1", "0295484aea2cd54ad0cc4f09fbea5a3285c3361d7db716809d1421a39adb8b91"),
            ("linux-x64", "xz", "xz-utils", "xz", "/usr/bin/xz", "5.4.1-1", "31c8422d8432de91ffa9b3713743c98cb8011c561546c76759600c9476357dc0"),
            ("linux-x64", "lz4", "lz4", "lz4", "/usr/bin/lz4", "1.9.4-1", "d7958b8fa6659cb45852d061dcecd84a897f26a0f98a8b52d787b35db3791dbb"),
            ("linux-x64", "brotli", "brotli", "brotli", "/usr/bin/brotli", "1.0.9-2+b6", "b111a83afdee0f0555f988968d2de0e7ddb4561db36ef31f71a1d1d95af937ce"),
            ("linux-x64", "zstd", "zstd", "zstd", "/usr/bin/zstd", "1.5.4+dfsg2-5", "cee5aaa2d86c0bf168fc57b759439f5900f2a3b55a9250271c473a7b08e3d3e3"),
            ("linux-x64", "uncompress", "ncompress", "uncompress", "/usr/bin/uncompress", "4.2.4.6-6", "55c2f67ca4c3cca0ebac659f0075461dd671ec4937ecd6c71123bb49ed322ebd"),
            ("linux-x64", "unzip", "unzip", "unzip", "/usr/bin/unzip", "6.0-28", "e2f7d58ad17fb5ad25d4e3cfb72870089dec4a54805d83650b8fd1b648a0c29b"),
            ("linux-x64", "python3", "python3-minimal", "python3", "/usr/bin/python3", "3.11.2-1+b1", "c6e1f1ef67ab331cbb83bfbd5bbb9b766fbb2228ce848b038141cb7d2cad3158"),
            ("linux-arm64", "gzip", "gzip", "gzip", "/usr/bin/gzip", "1.12-1", "d3afaebcb97bf6fa214a813d89b108f48955665ea596228340ec80580ee55a0e"),
            ("linux-arm64", "bzip2", "bzip2", "bzip2", "/usr/bin/bzip2", "1.0.8-5+b1", "40cbbed6f2decef80c0620931b095623705422c19cb5c14b8b27f125a3a5be21"),
            ("linux-arm64", "xz", "xz-utils", "xz", "/usr/bin/xz", "5.4.1-1", "26c98f8bc8f57e82b65b015cb699088ea1da2fc29557f4bc97bbce4fc0069cc8"),
            ("linux-arm64", "lz4", "lz4", "lz4", "/usr/bin/lz4", "1.9.4-1", "0cb356909741a31909cebbae5120e1faff61c265857b18f35fe96a157f1fd377"),
            ("linux-arm64", "brotli", "brotli", "brotli", "/usr/bin/brotli", "1.0.9-2+b6", "bc0ff60a77a83a039e136e6b77d4373ef5a01f233f7d42360aef5ce9a70194e5"),
            ("linux-arm64", "zstd", "zstd", "zstd", "/usr/bin/zstd", "1.5.4+dfsg2-5", "f3336accc2f38ffc03c3ee4b123b53d06ce14abfb4d19028841883c408fdbaf2"),
            ("linux-arm64", "uncompress", "ncompress", "uncompress", "/usr/bin/uncompress", "4.2.4.6-6", "55c2f67ca4c3cca0ebac659f0075461dd671ec4937ecd6c71123bb49ed322ebd"),
            ("linux-arm64", "unzip", "unzip", "unzip", "/usr/bin/unzip", "6.0-28", "b607a8f5f7056dcccb7d81e83cd25115d73e0ef07e337581a4f591ceb01e6f41"),
            ("linux-arm64", "python3", "python3-minimal", "python3", "/usr/bin/python3", "3.11.2-1+b1", "ef8ea6cdde1ed5696b130d90a07026a3088e0c767166a3a627233da845a9fae9"),
        ];

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string ciWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        string releaseGateWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-gates.yml"));
        string resolver = File.ReadAllText(Path.Combine(root, "eng", "resolve-linux-prereqs.sh"));
        string hostInstaller = File.ReadAllText(Path.Combine(root, "eng", "install-linux-host-prereqs.sh"));
        string design = File.ReadAllText(Path.Combine(root, "docs", "DESIGN.md"));
        string snapshotDate = ReadTopLevelTomlValue(prerequisiteLock, "linux_snapshot");
        string snapshotUrl = "http://snapshot.debian.org/archive/debian/" + snapshotDate.Replace("-", string.Empty, StringComparison.Ordinal) + "T000000Z";

        Assert.Equal("2026-05-31", snapshotDate);
        Assert.Contains("base_image = \"debian:bookworm-slim\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("index_digest = \"sha256:0104b334637a5f19aa9c983a91b54c89887c0984081f2068983107a6f6c21eeb\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("amd64_digest = \"sha256:b29f74a267526ae6ea104eed6c46133b0ca70ce812525df8cd5817698f0a624a\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("arm64_digest = \"sha256:f1433d3ee18e12f45682b29d91b6356e54e40d6b47f5f8ac81e80f35cca8cfe7\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("snapshot_url = \"" + snapshotUrl + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("LINUX_SNAPSHOT_URL: \"" + snapshotUrl + "\"", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("SNAPSHOT_DATE=\"2026-05-31\"", resolver, StringComparison.Ordinal);
        Assert.Contains("date `2026-05-31`", design, StringComparison.Ordinal);
        Assert.Contains("deb http://snapshot.debian.org/archive/debian/20260531T000000Z bookworm main", design, StringComparison.Ordinal);
        Assert.Contains("libc6_version = \"2.36-9+deb12u14\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("libc_bin_version = \"2.36-9+deb12u14\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("unzip \\", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("unzip \\", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("unzip \\", hostInstaller, StringComparison.Ordinal);
        Assert.Contains("unzip:unzip", resolver, StringComparison.Ordinal);
        Assert.Contains("python3 \\", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("python3 \\", releaseGateWorkflow, StringComparison.Ordinal);
        Assert.Contains("python3 \\", hostInstaller, StringComparison.Ordinal);
        Assert.Contains("python3:python3-minimal", resolver, StringComparison.Ordinal);

        for (int index = 0; index < tools.Length; index++)
        {
            (string rid, string name, string package, string binary, string path, string version, string sha256) = tools[index];
            string block = string.Join(
                "\n",
                "[[tool.linux]]",
                "rid = \"" + rid + "\"",
                "name = \"" + name + "\"",
                "package = \"" + package + "\"",
                "binary = \"" + binary + "\"",
                "path = \"" + path + "\"",
                "version = \"" + version + "\"",
                "sha256 = \"" + sha256 + "\"");
            Assert.Contains(block, prerequisiteLock, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies hosted Linux CI checks installed prerequisite tools against the lockfile.
    /// </summary>
    [Fact]
    public void LinuxPrerequisiteVerificationChecksInstalledToolHashes()
    {
        string root = FindRepositoryRoot();
        string verifyScript = File.ReadAllText(Path.Combine(root, "eng", "verify-linux-prereqs.sh"));
        string resolveScript = File.ReadAllText(Path.Combine(root, "eng", "resolve-linux-prereqs.sh"));

        Assert.Contains("[[tool.linux]]", verifyScript, StringComparison.Ordinal);
        Assert.Contains("dpkg-query -W -f='${Version}' \"$package\"", verifyScript, StringComparison.Ordinal);
        Assert.Contains("sha256_file \"$path\"", verifyScript, StringComparison.Ordinal);
        Assert.Contains("command -v \"$binary\"", verifyScript, StringComparison.Ordinal);
        Assert.Contains("LINUX_SNAPSHOT_URL", verifyScript, StringComparison.Ordinal);
        Assert.Contains("LINUX_LIBC_VERSION", verifyScript, StringComparison.Ordinal);
        Assert.Contains("dpkg-query -W -f='${Version}' libc6", verifyScript, StringComparison.Ordinal);
        Assert.Contains("Unpinned Debian source remains", verifyScript, StringComparison.Ordinal);
        Assert.Contains("--no-install-recommends libc6='\"$LIBC_VERSION\"' libc-bin='\"$LIBC_VERSION\"'", resolveScript, StringComparison.Ordinal);
        Assert.DoesNotContain("--allow-downgrades", resolveScript, StringComparison.Ordinal);
        Assert.Contains("rm -f /etc/apt/sources.list.d/*.sources /etc/apt/sources.list.d/*.list", resolveScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the macOS benchmark tool in the prerequisite lock matches the local binary.
    /// </summary>
    [Fact]
    public void MacosHyperfineToolMatchesPinnedHash()
    {
        const string name = "hyperfine";
        const string version = "1.20.0";
        const string path = "/opt/homebrew/bin/hyperfine";
        const string expectedSha256 = "B7272E98214E951452BCBC81EAF0B8F0DB391DCE05B58CE8AB42985B632C5E94";
        const string sourceUrl = "https://github.com/sharkdp/hyperfine/archive/refs/tags/v1.20.0.tar.gz";
        const string sourceSha256 = "f90c3b096af568438be7da52336784635a962c9822f10f98e5ad11ae8c7f5c64";
        const string bottleUrl = "https://ghcr.io/v2/homebrew/core/hyperfine/blobs/sha256:2a79829da44dc03e12ea4977b6bfa122cea8487e741c24a7fbcc7ce6a4788db3";
        const string bottleSha256 = "2a79829da44dc03e12ea4977b6bfa122cea8487e741c24a7fbcc7ce6a4788db3";

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));

        Assert.Contains("name = \"" + name + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("version = \"" + version + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"" + path + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("source_url = \"" + sourceUrl + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("source_sha256 = \"" + sourceSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("bottle_url = \"" + bottleUrl + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("bottle_sha256 = \"" + bottleSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("sha256 = \"" + expectedSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);

        if (OperatingSystem.IsMacOS())
        {
            Assert.True(File.Exists(path), "Missing macOS prerequisite tool: " + path);
            (int exitCode, string output, string error) = RunProcess(path, ["--version"]);
            Assert.True(exitCode == 0, error);
            Assert.Equal("hyperfine " + version, output.Trim());

            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            Assert.Equal(expectedSha256, Convert.ToHexString(hash));
        }
    }

    /// <summary>
    /// Verifies hosted release gates install hyperfine from checksum-verified pinned Homebrew artifacts.
    /// </summary>
    [Fact]
    public void HostedReleaseGatesProvisionPinnedHyperfine()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-gates.yml"));
        string script = File.ReadAllText(Path.Combine(root, "eng", "setup-hyperfine.sh"));

        Assert.Contains("Install pinned hyperfine", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/setup-hyperfine.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("brew install hyperfine", workflow, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"version\"", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"source_url\"", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"source_sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"bottle_url\"", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"bottle_sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("read_lock_table_value \"tool.macos\" \"$NAME\" \"sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("brew info --json=v2 \"$NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("verify_homebrew_metadata", script, StringComparison.Ordinal);
        Assert.Contains("retry_command()", script, StringComparison.Ordinal);
        Assert.Contains("retry_command brew fetch --formula --build-from-source \"$NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("check_file_hash \"hyperfine source archive\"", script, StringComparison.Ordinal);
        Assert.Contains("retry_command brew fetch --formula \"$NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("check_file_hash \"hyperfine bottle archive\"", script, StringComparison.Ordinal);
        Assert.Contains("check_file_hash \"macOS tool hyperfine\"", script, StringComparison.Ordinal);
        Assert.Contains("version_matches \"$PATH_VALUE\" \"$VERSION\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", script, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the external benchmark corpora have pinned fetch inputs.
    /// </summary>
    [Fact]
    public void ExternalBenchmarkCorporaHavePinnedInputs()
    {
        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string preflight = File.ReadAllText(Path.Combine(root, "eng", "preflight.sh"));

        Assert.DoesNotContain("resolved@", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("require_literal \"$sha256\" \"corpus $name sha256\"", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved@*)\n                continue", preflight, StringComparison.Ordinal);
        Assert.Contains("name = \"opensubtitles-en\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("kind = \"file\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains(
            "archive_url = \"https://object.pouta.csc.fi/OPUS-OpenSubtitles/v2016/mono/en.txt.gz\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains("archive_path = \"artifacts/corpora/opensubtitles/en.txt.gz\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains(
            "archive_sha256 = \"7c169ffa7742fd7f670c176ba8c290b74bcc650784e585e2ef60061376c8fff1\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains("path = \"artifacts/corpora/opensubtitles/en.txt\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains(
            "sha256 = \"a84b1e0c66645c429ff356510dc872d5d9cca1c5a02a21d6eee3cff24d4781bb\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains("bytes = \"9968530111\"", prerequisiteLock, StringComparison.Ordinal);

        Assert.Contains("name = \"linux-kernel\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("kind = \"tree\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("commit = \"84e57d292203a45c96dbcb2e6be9dd80961d981a\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains(
            "archive_url = \"https://codeload.github.com/BurntSushi/linux/tar.gz/84e57d292203a45c96dbcb2e6be9dd80961d981a\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains(
            "archive_path = \"artifacts/corpora/linux/linux-84e57d292203a45c96dbcb2e6be9dd80961d981a.tar.gz\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains(
            "archive_sha256 = \"8779f9318fb896f64f7a876d7ff9c152925e82c17690281eb1ec6ce587275054\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains(
            "tree_path = \"artifacts/corpora/linux/linux-84e57d292203a45c96dbcb2e6be9dd80961d981a\"",
            prerequisiteLock,
            StringComparison.Ordinal);
        Assert.Contains(
            "tree_sha256 = \"c104036f61aa7eba26da621738424e2e35f2c12372858abc345c39bbd9ecd116\"",
            prerequisiteLock,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the hyperfine benchsuite covers every release-gate workload class.
    /// </summary>
    [Fact]
    public void HyperfineBenchSuiteCoversReleaseGateWorkloads()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "bench", "run-hyperfine.sh"));
        string readme = File.ReadAllText(Path.Combine(root, "bench", "README.md"));

        Assert.Contains("subtitles_en_literal", script, StringComparison.Ordinal);
        Assert.Contains("subtitles_en_regex", script, StringComparison.Ordinal);
        Assert.Contains("linux_recursive_literal", script, StringComparison.Ordinal);
        Assert.Contains("linux_many_small_parallel", script, StringComparison.Ordinal);
        Assert.Contains("cold_version", script, StringComparison.Ordinal);
        Assert.Contains("cold_tiny_search", script, StringComparison.Ordinal);
        Assert.Contains("peak RSS <= 1.50x or rg + 32MiB", script, StringComparison.Ordinal);
        Assert.Contains("check_ratio_gate", script, StringComparison.Ordinal);
        Assert.Contains("hyperfine_json_metric", script, StringComparison.Ordinal);
        Assert.Contains("hyperfine_json_median_memory", script, StringComparison.Ordinal);
        Assert.Contains("median peak RSS ratio", script, StringComparison.Ordinal);
        Assert.Contains("GATE_OPENSUBTITLES_RUNS=\"5\"", script, StringComparison.Ordinal);
        Assert.Contains("GATE_OPENSUBTITLES_WARMUP=\"2\"", script, StringComparison.Ordinal);
        Assert.Contains("gate_opensubtitles_runs", script, StringComparison.Ordinal);
        Assert.Contains("gate_opensubtitles_warmup", script, StringComparison.Ordinal);
        Assert.Contains("GATE_TREE_RUNS=\"5\"", script, StringComparison.Ordinal);
        Assert.Contains("GATE_TREE_WARMUP=\"3\"", script, StringComparison.Ordinal);
        Assert.Contains("gate_tree_runs", script, StringComparison.Ordinal);
        Assert.Contains("gate_tree_warmup", script, StringComparison.Ordinal);
        Assert.Contains("median ratio", script, StringComparison.Ordinal);
        Assert.Contains("median wall time", readme, StringComparison.Ordinal);
        Assert.Contains("median per-run peak RSS", readme, StringComparison.Ordinal);
        Assert.Contains("OpenSubtitles workloads use five runs and two", readme, StringComparison.Ordinal);
        Assert.Contains("Linux-tree workloads use five runs and three", readme, StringComparison.Ordinal);
        Assert.Contains("run_pair_no_shell", script, StringComparison.Ordinal);
        Assert.Contains("-N", script, StringComparison.Ordinal);
        Assert.Contains("make_cold_tiny_corpus", script, StringComparison.Ordinal);
        Assert.Contains("resolve_hyperfine()", script, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"path\" \"ripgrep_rg_path\"", script, StringComparison.Ordinal);
        Assert.Contains("read_ripgrep_oracle_value \"sha256\" \"ripgrep_rg_sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("if [ \"$MODE\" = \"gate\" ]; then", script, StringComparison.Ordinal);
        Assert.Contains("Missing pinned hyperfine path in tests/PREREQS.lock.", script, StringComparison.Ordinal);
        Assert.Contains("check_file_hash \"hyperfine\" \"$pinned_path\" \"$pinned_sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("check_tool_version \"hyperfine\" \"$pinned_path\" \"hyperfine $pinned_version\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("read_lock_table_value \"tool.macos\" \"hyperfine\" \"path\")\" || HYPERFINE=\"$(command -v hyperfine", script, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved@fetch", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the memchr port has explicit SIMD-gated search paths required by the design.
    /// </summary>
    [Fact]
    public void MemchrSearchIncludesRequiredSimdGates()
    {
        string root = FindRepositoryRoot();
        string memchrSearch = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata.Memmem", "MemchrSearch.cs"));
        string upstream = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata.Memmem", "UPSTREAM.md"));

        Assert.Contains("Avx512BW.IsSupported", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Avx2.IsSupported", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Sse2.IsSupported", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("AdvSimd.IsSupported", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("FindScalar", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Find2Vector512", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Find3Vector512", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("FindReverseVector512", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Find2ReverseVector512", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("Find3ReverseVector512", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("ExtractMostSignificantBits", memchrSearch, StringComparison.Ordinal);
        Assert.Contains("AVX-512", upstream, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the design-required bytecount port has SIMD gates and is used by multiline accounting.
    /// </summary>
    [Fact]
    public void ByteCounterIncludesRequiredSimdGates()
    {
        string root = FindRepositoryRoot();
        string byteCounter = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata", "ByteCounter.cs"));
        string multiline = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "MultilineSearchOperations.cs"));
        string pcre2 = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "Pcre2SearchOperations.cs"));
        string json = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "JsonSearchOperations.cs"));
        string upstream = File.ReadAllText(Path.Combine(root, "src", "Scout.Automata", "UPSTREAM.md"));

        Assert.Contains("Avx512BW.IsSupported", byteCounter, StringComparison.Ordinal);
        Assert.Contains("Avx2.IsSupported", byteCounter, StringComparison.Ordinal);
        Assert.Contains("Sse2.IsSupported", byteCounter, StringComparison.Ordinal);
        Assert.Contains("AdvSimd.IsSupported", byteCounter, StringComparison.Ordinal);
        Assert.Contains("CountVector512", byteCounter, StringComparison.Ordinal);
        Assert.Contains("BitOperations.PopCount", byteCounter, StringComparison.Ordinal);
        Assert.Contains("ByteCounter.Count(bytes, (byte)'\\n')", multiline, StringComparison.Ordinal);
        Assert.Contains("CountLineTerminators", pcre2, StringComparison.Ordinal);
        Assert.Contains("ByteCounter.Count(bytes, GetPcre2LineTerminatorByte(lineTerminator))", pcre2, StringComparison.Ordinal);
        Assert.Contains("ByteCounter.Count(bytes", json, StringComparison.Ordinal);
        Assert.Contains("SIMD byte", upstream, StringComparison.Ordinal);
        Assert.Contains("counting", upstream, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the regex conformance corpus files are pinned by hash.
    /// </summary>
    [Fact]
    public void RegexCorpusFilesMatchPinnedHashes()
    {
        (string Name, string RelativePath, string Sha256)[] corpora =
        [
            (
                "regex-1.12.2-misc",
                "upstream/regex-1.12.2/testdata/misc.toml",
                "32C9591655C6FB118DFEFCB4DE49A04820A63CB960533DFC2538CDAABF4F4047"),
            (
                "regex-1.12.2-flags",
                "upstream/regex-1.12.2/testdata/flags.toml",
                "9A7E001808195C84F2A7D3E18BC0A82C7386E60F03A616E99AF00C3F7F2C3FD4"),
            (
                "regex-1.12.2-iter",
                "upstream/regex-1.12.2/testdata/iter.toml",
                "6875460302974A5B3073A7304A865C45ABA9653C54AFEA2C4D26E1EA248A81F7"),
            (
                "regex-1.12.2-empty",
                "upstream/regex-1.12.2/testdata/empty.toml",
                "738DBE92FBD8971385A1CF3AFFB0E956E5B692C858B9B48439D718F10801C08E"),
            (
                "regex-1.12.2-crazy",
                "upstream/regex-1.12.2/testdata/crazy.toml",
                "A146E2D2E23F1A57168979D9B1FC193C2BA38DCA66294B61140D6D2A2958EC86"),
            (
                "regex-1.12.2-multiline",
                "upstream/regex-1.12.2/testdata/multiline.toml",
                "EB07CF5427E6DDBCF61F4CC64C2D74FF41B5EF75EF857959651B20196F3CD157"),
            (
                "regex-1.12.2-line-terminator",
                "upstream/regex-1.12.2/testdata/line-terminator.toml",
                "02148068137B69D95587966917BDF0697BF7EB41AD6D47387F2EB30F67D04FD9"),
            (
                "regex-1.12.2-anchored",
                "upstream/regex-1.12.2/testdata/anchored.toml",
                "7A1B5CD81DEED2099796A451BF764A3F9BD21F0D60C0FA46ACCD3A35666866F2"),
            (
                "regex-1.12.2-substring",
                "upstream/regex-1.12.2/testdata/substring.toml",
                "48122D9F3477ED81F95E3AD42C06E9BB25F849B66994601A75CEAE0693B81866"),
            (
                "regex-1.12.2-bytes",
                "upstream/regex-1.12.2/testdata/bytes.toml",
                "1D84179165FD25F3B94BD2BFBEB43FC8A162041F7BF98B717E0F85CEF7FB652B"),
            (
                "regex-1.12.2-crlf",
                "upstream/regex-1.12.2/testdata/crlf.toml",
                "D19CF22756434D145DD20946C00AF01C102A556A252070405C3C8294129D9ECE"),
            (
                "regex-1.12.2-earliest",
                "upstream/regex-1.12.2/testdata/earliest.toml",
                "D561E643623EE1889B5B049FDCF3C7CB71B0C746D7EB822DDBD09D0ACDA2620B"),
            (
                "regex-1.12.2-expensive",
                "upstream/regex-1.12.2/testdata/expensive.toml",
                "5CE2F60209C99CDD2CDCB9D3069D1D5CA13D5E08A85E913EFE57267B2F5F0E9D"),
            (
                "regex-1.12.2-leftmost-all",
                "upstream/regex-1.12.2/testdata/leftmost-all.toml",
                "903BFBEFF888B7664296F4D5AA367CE53D1DAFE249AB0A3359223AE94D596396"),
            (
                "regex-1.12.2-no-unicode",
                "upstream/regex-1.12.2/testdata/no-unicode.toml",
                "D209DA04506900FD5F69E48170CDDAAD0702355AC6176C3A75AB3FF96974457C"),
            (
                "regex-1.12.2-overlapping",
                "upstream/regex-1.12.2/testdata/overlapping.toml",
                "5D96497A7233566D40B05BA22047E483FA8662E45515A9BE86DA45CF6C28703A"),
            (
                "regex-1.12.2-regex-lite",
                "upstream/regex-1.12.2/testdata/regex-lite.toml",
                "FECCA7CC8C9CEA2E1F84F846A89FD9B3CA7011C83698211A2EEDA8924DEB900C"),
            (
                "regex-1.12.2-regression",
                "upstream/regex-1.12.2/testdata/regression.toml",
                "6006EF4FCFBFD7155CE5CE8B8427904F7261C5549396F20CB065C0294733686D"),
            (
                "regex-1.12.2-set",
                "upstream/regex-1.12.2/testdata/set.toml",
                "DFD265DC1AEE80026E881616840DF0236AE9ABF12467D7EC0E141A52C236128C"),
            (
                "regex-1.12.2-unicode",
                "upstream/regex-1.12.2/testdata/unicode.toml",
                "7E4B013039B0CDD85FA73F32D15D096182FE901643D4E40C0910087A736CD46D"),
            (
                "regex-1.12.2-utf8",
                "upstream/regex-1.12.2/testdata/utf8.toml",
                "2EABCE0582BCACB2073E08BBE7CA413F096D14D06E917B107949691E24F84B20"),
            (
                "regex-1.12.2-word-boundary-special",
                "upstream/regex-1.12.2/testdata/word-boundary-special.toml",
                "7D0EA2F796478D1CA2A6954430CB1CFBD04031A182F8611CB50A7C73E443CE33"),
            (
                "regex-1.12.2-word-boundary",
                "upstream/regex-1.12.2/testdata/word-boundary.toml",
                "51BC1C498AB825420340A2DD3E6623DE4054937BA6D5020FF8CD14B1C1E45271"),
        ];

        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        for (int index = 0; index < corpora.Length; index++)
        {
            (string name, string relativePath, string expectedSha256) = corpora[index];
            Assert.Contains("name = \"" + name + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("path = \"" + relativePath + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("sha256 = \"" + expectedSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);

            string path = Path.Combine(root, relativePath);
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            Assert.Equal(expectedSha256, Convert.ToHexString(hash));
        }
    }

    /// <summary>
    /// Verifies vendored conformance corpus pins do not depend on developer-local Cargo cache paths.
    /// </summary>
    [Fact]
    public void PrerequisiteLockUsesRepoRelativeConformanceCorpusPaths()
    {
        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));

        Assert.DoesNotContain("/.cargo/registry/", prerequisiteLock, StringComparison.Ordinal);
        Assert.DoesNotContain("\\.cargo\\registry\\", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/regex-1.12.2/testdata/misc.toml\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/encoding_rs-0.8.35/src/test_data/big5_in.txt\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/encoding_rs-0.8.35/src/test_labels_names.rs\"", prerequisiteLock, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies every vendored upstream conformance corpus file is pinned in the prerequisite lock.
    /// </summary>
    [Fact]
    public void VendoredConformanceCorpusFilesMatchPrerequisiteLock()
    {
        string root = FindRepositoryRoot();
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string[] corpusRoots =
        [
            Path.Combine(root, "upstream", "regex-1.12.2", "testdata"),
            Path.Combine(root, "upstream", "encoding_rs-0.8.35", "src"),
            Path.Combine(root, "upstream", "ripgrep-4857d6fa", "tests"),
        ];

        foreach (string corpusRoot in corpusRoots)
        {
            foreach (string path in Directory.EnumerateFiles(corpusRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                string expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

                Assert.Contains("path = \"" + relativePath + "\"", prerequisiteLock, StringComparison.Ordinal);
                Assert.Contains("sha256 = \"" + expectedSha256 + "\"", prerequisiteLock, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Verifies the ported ripgrep integration tests use vendored upstream fixtures.
    /// </summary>
    [Fact]
    public void PortedRipgrepTestCorpusIsVendored()
    {
        string root = FindRepositoryRoot();
        string coverageTest = File.ReadAllText(Path.Combine(root, "tests", "Scout.Differential.Tests", "PortedRgTestCoverageTests.cs"));
        string portedTests = File.ReadAllText(Path.Combine(root, "tests", "Scout.Differential.Tests", "PortedRgTests.cs"));
        string prerequisiteLock = File.ReadAllText(Path.Combine(root, "tests", "PREREQS.lock"));
        string attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));
        string testsRoot = Path.Combine(root, "upstream", "ripgrep-4857d6fa", "tests");

        Assert.DoesNotContain("/Users/brandon/src/ripgrep/tests", coverageTest, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/brandon/src/ripgrep/tests", portedTests, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(testsRoot, "regression.rs")));
        Assert.True(File.Exists(Path.Combine(testsRoot, "data", "sherlock-nul.txt")));
        Assert.Contains("upstream/ripgrep-4857d6fa/tests/** -whitespace", attributes, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/ripgrep-4857d6fa/tests/regression.rs\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/ripgrep-4857d6fa/tests/data/sherlock-nul.txt\"", prerequisiteLock, StringComparison.Ordinal);
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
            @"dotnet_diagnostic\.(SCOUT[0-9]+|IDE0130)\.severity\s*=\s*(?!error\b)[^\s#;]+",
            @"dotnet_analyzer_diagnostic\.category-Scout\.Structure\.severity\s*=\s*(?!error\b)[^\s#;]+",
        ];

        return new Regex(string.Join("|", patterns), RegexOptions.CultureInvariant);
    }

    private static Regex CreateForbiddenProjectProvenanceDeferralPattern()
    {
        string[] patterns =
        [
            Regex.Escape("Later " + "milestone"),
            Regex.Escape("Remaining " + "surface"),
            Regex.Escape("future " + "work"),
            Regex.Escape("not " + "implemented"),
            Regex.Escape("out-" + "of-" + "scope"),
            Regex.Escape("deferred"),
            Regex.Escape("stubbed"),
            Regex.Escape("scaffold"),
        ];

        return new Regex(string.Join("|", patterns), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static (string Label, Regex Pattern)[] CreateForbiddenTestWaiverPatterns()
    {
        return
        [
            ("xUnit skip attribute", XUnitSkipAttributePattern()),
            ("xUnit explicit attribute", XUnitExplicitAttributePattern()),
            ("ignore or explicit attribute", IgnoreOrExplicitAttributePattern()),
            ("quarantine or skip trait", QuarantineOrSkipTraitPattern()),
            ("runtime test skip", RuntimeTestSkipPattern()),
            ("skip exception", SkipExceptionPattern()),
            ("fixture capability return", FixtureCapabilityReturnPattern()),
            ("platform guard return", PlatformGuardReturnPattern()),
        ];
    }

    [GeneratedRegex(@"\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?:Fact|Theory)\s*\([^)\r\n]*\bSkip\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex XUnitSkipAttributePattern();

    [GeneratedRegex(@"\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?:Fact|Theory)\s*\([^)\r\n]*\bExplicit\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex XUnitExplicitAttributePattern();

    [GeneratedRegex(@"\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?:Ignore|Explicit)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreOrExplicitAttributePattern();

    [GeneratedRegex(@"\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?Trait\s*\([^)\r\n]*(?:Quarantine|Skipped|Ignored)[^)\r\n]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex QuarantineOrSkipTraitPattern();

    [GeneratedRegex(@"\bAssert\s*\.\s*Skip\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeTestSkipPattern();

    [GeneratedRegex(@"\bthrow\s+new\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)?SkipException\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex SkipExceptionPattern();

    [GeneratedRegex(@"\[\[package\]\]\s+name = ""(?<name>[^""]+)""\s+version = ""(?<version>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex CargoLockPackagePattern();

    [GeneratedRegex(@"if\s*\([^{;]*(?:TryCreateDirectorySymlink|TryCreateFileSymlink)[^{;]*\)\s*\{\s*return;\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex FixtureCapabilityReturnPattern();

    [GeneratedRegex(@"if\s*\([^{;]*OperatingSystem\.[^{;]*\)\s*\{\s*return;\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformGuardReturnPattern();

    [GeneratedRegex(@"^\s*(?:runs-on|runner):\s*(?<label>[^\r\n#]+?)\s*(?:#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowRunnerLabelPattern();

    private static IEnumerable<string> EnumerateWorkflowRunnerLabels(string workflow)
    {
        using var reader = new StringReader(workflow);
        while (reader.ReadLine() is { } line)
        {
            Match match = WorkflowRunnerLabelPattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            string label = match.Groups["label"].Value.Trim().Trim('"', '\'');
            if (label.StartsWith("${{", StringComparison.Ordinal))
            {
                continue;
            }

            yield return label;
        }
    }

    private static string ReadMarkdownSection(string markdown, string heading)
    {
        string headingLine = "## " + heading;
        using var reader = new StringReader(markdown);
        bool inSection = false;
        var lines = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                {
                    break;
                }

                inSection = string.Equals(line.Trim(), headingLine, StringComparison.Ordinal);
                continue;
            }

            if (inSection)
            {
                lines.Add(line);
            }
        }

        if (!inSection && lines.Count == 0)
        {
            throw new InvalidOperationException("Missing markdown section: " + headingLine);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ReadTopLevelTomlValue(string toml, string key)
    {
        string prefix = key + " = ";
        using var reader = new StringReader(toml);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
            {
                break;
            }

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = line.Substring(prefix.Length).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        throw new InvalidOperationException("Missing top-level TOML value: " + key);
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

    private static IEnumerable<string> EnumerateProjectProvenanceFiles(string root)
    {
        string sourceRoot = Path.Combine(root, "src");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "UPSTREAM.md", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (!ContainsPathSegment(relativePath, "bin") && !ContainsPathSegment(relativePath, "obj"))
            {
                yield return path;
            }
        }
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

    private static IEnumerable<string> EnumerateRuntimeSourceFiles(string root)
    {
        string sourceRoot = Path.Combine(root, "src");
        string sourceGeneratorRoot = Path.Combine("src", "Scout.SourceGen");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (ContainsPathSegment(relativePath, "bin") ||
                ContainsPathSegment(relativePath, "obj") ||
                relativePath.StartsWith(sourceGeneratorRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(sourceGeneratorRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
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
