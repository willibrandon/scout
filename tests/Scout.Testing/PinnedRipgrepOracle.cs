using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Scout;

internal static class PinnedRipgrepOracle
{
    internal const string ExecutablePathEnvironmentVariable = "SCOUT_TEST_RIPGREP_PATH";

    private const string OracleTableName = "ripgrep_oracle";

    private static readonly Lazy<string> VerifiedExecutablePath = new(ResolveAndVerifyDefaultExecutablePath);
    private static readonly Lazy<string> HostRuntimeIdentifier = new(ComputeHostRuntimeIdentifier);
    private static readonly Lazy<string> HostOracleEnvironmentValue = new(ComputeHostOracleEnvironment);

    internal static string ExecutablePath => VerifiedExecutablePath.Value;

    internal static string HostRid => HostRuntimeIdentifier.Value;

    internal static string HostOracleEnvironment => HostOracleEnvironmentValue.Value;

    internal static string DefaultExecutablePath => ResolveOraclePath(ReadHostOracleValue("path", "ripgrep_rg_path"));

    internal static string ExpectedSha256 => ReadHostOracleValue("sha256", "ripgrep_rg_sha256");

    internal static string ReferenceRoot => DeriveReferenceRoot(DefaultExecutablePath);

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

    internal static string ResolveAndVerifyExecutablePath(
        string tablePathKey,
        string rootPathKey,
        string tableSha256Key,
        string rootSha256Key,
        string environmentVariable,
        string displayName)
    {
        string defaultPath = ResolveOraclePath(ReadHostOracleValue(tablePathKey, rootPathKey));
        string? overridePath = Environment.GetEnvironmentVariable(environmentVariable);
        string executablePath = string.IsNullOrWhiteSpace(overridePath) ? defaultPath : ResolveOraclePath(overridePath);
        string expectedSha256 = ReadHostOracleValue(tableSha256Key, rootSha256Key);
        if (!IsLowercaseSha256(expectedSha256))
        {
            throw new InvalidOperationException(rootSha256Key + " must be a literal lowercase SHA-256 in tests/PREREQS.lock.");
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

    private static bool IsLowercaseSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    internal static string ReadPrerequisiteValue(string key)
    {
        string prerequisiteLock = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PREREQS.lock"));
        return ReadPrerequisiteValue(prerequisiteLock, key);
    }

    internal static string ReadHostOracleValue(string tableKey, string rootKey)
    {
        string prerequisiteLock = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PREREQS.lock"));
        if (TryReadHostOracleValue(prerequisiteLock, tableKey, HostOracleEnvironment, out string value))
        {
            return value;
        }

        if (TryReadHostOracleValue(prerequisiteLock, tableKey, null, out value))
        {
            return value;
        }

        if (HasOracleTable(prerequisiteLock))
        {
            throw new InvalidOperationException("Could not find " + OracleTableName + "." + tableKey + " for host RID " + HostRid + " in tests/PREREQS.lock.");
        }

        return ReadPrerequisiteValue(prerequisiteLock, rootKey);
    }

    private static string ResolveAndVerifyDefaultExecutablePath()
    {
        return ResolveAndVerifyExecutablePath(
            "path",
            "ripgrep_rg_path",
            "sha256",
            "ripgrep_rg_sha256",
            ExecutablePathEnvironmentVariable,
            "ripgrep");
    }

    private static string ComputeHostRuntimeIdentifier()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Unsupported process architecture for pinned ripgrep oracle: " + RuntimeInformation.ProcessArchitecture),
        };

        if (OperatingSystem.IsMacOS())
        {
            return "osx-" + architecture;
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux-" + architecture;
        }

        if (OperatingSystem.IsWindows())
        {
            return "win-" + architecture;
        }

        throw new PlatformNotSupportedException("Unsupported OS for pinned ripgrep oracle: " + RuntimeInformation.OSDescription);
    }

    private static string ComputeHostOracleEnvironment()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            ? "github-actions"
            : "local";
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

    private static bool TryReadHostOracleValue(string text, string key, string? environment, out string value)
    {
        using var reader = new StringReader(text);
        bool inOracle = false;
        bool matchedRid = false;
        bool matchedEnvironment = environment is null;
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed == "[[" + OracleTableName + "]]")
            {
                inOracle = true;
                matchedRid = false;
                matchedEnvironment = environment is null;
                continue;
            }

            if (inOracle && trimmed.StartsWith('['))
            {
                inOracle = false;
                matchedRid = false;
                matchedEnvironment = environment is null;
            }

            if (!inOracle)
            {
                continue;
            }

            if (TryReadAssignment(trimmed, "rid", out string rid))
            {
                matchedRid = string.Equals(rid, HostRid, StringComparison.Ordinal);
                continue;
            }

            if (TryReadAssignment(trimmed, "environment", out string tableEnvironment))
            {
                matchedEnvironment = environment is not null && string.Equals(tableEnvironment, environment, StringComparison.Ordinal);
                continue;
            }

            if (matchedRid && matchedEnvironment && TryReadAssignment(trimmed, key, out value))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool HasOracleTable(string text)
    {
        return text.Contains("[[" + OracleTableName + "]]", StringComparison.Ordinal);
    }

    private static bool TryReadAssignment(string line, string key, out string value)
    {
        string prefix = key + " = \"";
        if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith('"'))
        {
            value = line[prefix.Length..^1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal static string ResolveOraclePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(FindRepositoryRoot(), path));
    }

    private static string DeriveReferenceRoot(string executablePath)
    {
        string fullPath = Path.GetFullPath(executablePath);
        string targetSegment = Path.DirectorySeparatorChar + "target" + Path.DirectorySeparatorChar;
        int targetIndex = fullPath.IndexOf(targetSegment, StringComparison.Ordinal);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("Could not derive ripgrep reference root from " + executablePath + ".");
        }

        return fullPath[..targetIndex];
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
