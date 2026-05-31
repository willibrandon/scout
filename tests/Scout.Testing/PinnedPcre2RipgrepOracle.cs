using System;
using System.Diagnostics;

namespace Scout;

internal static class PinnedPcre2RipgrepOracle
{
    internal const string ExecutablePathEnvironmentVariable = "SCOUT_TEST_PCRE2_RIPGREP_PATH";

    private static readonly Lazy<string> VerifiedExecutablePath = new(ResolveAndVerifyExecutablePath);

    internal static string ExecutablePath => VerifiedExecutablePath.Value;

    internal static string DefaultExecutablePath => PinnedRipgrepOracle.ReadPrerequisiteValue("ripgrep_pcre2_rg_path");

    internal static string ExpectedSha256 => PinnedRipgrepOracle.ReadPrerequisiteValue("ripgrep_pcre2_rg_sha256");

    internal static string ReportedVersion => PinnedRipgrepOracle.ReadPrerequisiteValue("ripgrep_pcre2_reported_version");

    internal static ProcessStartInfo CreateStartInfo(bool redirectStandardInput = false)
    {
        return new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
    }

    internal static void VerifyHash()
    {
        _ = VerifiedExecutablePath.Value;
    }

    private static string ResolveAndVerifyExecutablePath()
    {
        return PinnedRipgrepOracle.ResolveAndVerifyExecutablePath(
            "ripgrep_pcre2_rg_path",
            "ripgrep_pcre2_rg_sha256",
            ExecutablePathEnvironmentVariable,
            "PCRE2 ripgrep");
    }
}
