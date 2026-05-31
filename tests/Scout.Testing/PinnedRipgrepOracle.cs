using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Scout;

internal static class PinnedRipgrepOracle
{
    internal const string ExecutablePathEnvironmentVariable = "SCOUT_TEST_RIPGREP_PATH";

    private static readonly Lazy<string> VerifiedExecutablePath = new(ResolveAndVerifyDefaultExecutablePath);

    internal static string ExecutablePath => VerifiedExecutablePath.Value;

    internal static string DefaultExecutablePath => ReadPrerequisiteValue("ripgrep_rg_path");

    internal static string ExpectedSha256 => ReadPrerequisiteValue("ripgrep_rg_sha256");

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

    internal static string ResolveAndVerifyExecutablePath(string pathKey, string sha256Key, string environmentVariable, string displayName)
    {
        string defaultPath = ReadPrerequisiteValue(pathKey);
        string? overridePath = Environment.GetEnvironmentVariable(environmentVariable);
        string executablePath = string.IsNullOrWhiteSpace(overridePath) ? defaultPath : overridePath;
        string expectedSha256 = ReadPrerequisiteValue(sha256Key);
        if (expectedSha256.StartsWith("resolved@", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(sha256Key + " has not been frozen in tests/PREREQS.lock.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Missing pinned " + displayName + " binary: " + executablePath, executablePath);
        }

        byte[] hash = SHA256.HashData(File.ReadAllBytes(executablePath));
        string actualSha256 = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(expectedSha256, actualSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Pinned " + displayName + " hash mismatch for " + executablePath + ". Expected " + expectedSha256 + " but found " + actualSha256 + ".");
        }

        return executablePath;
    }

    internal static string ReadPrerequisiteValue(string key)
    {
        string prerequisiteLock = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PREREQS.lock"));
        return ReadPrerequisiteValue(prerequisiteLock, key);
    }

    private static string ResolveAndVerifyDefaultExecutablePath()
    {
        return ResolveAndVerifyExecutablePath("ripgrep_rg_path", "ripgrep_rg_sha256", ExecutablePathEnvironmentVariable, "ripgrep");
    }

    private static string ReadPrerequisiteValue(string text, string key)
    {
        string prefix = key + " = \"";
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith('"'))
            {
                continue;
            }

            return line[prefix.Length..^1];
        }

        throw new InvalidOperationException("Could not find " + key + " in tests/PREREQS.lock.");
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
