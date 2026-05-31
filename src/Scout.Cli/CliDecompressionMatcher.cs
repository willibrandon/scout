using System;
using System.Collections.ObjectModel;
using System.IO;

namespace Scout;

/// <summary>
/// Matches file paths to ripgrep-compatible decompression commands.
/// </summary>
public static class CliDecompressionMatcher
{
    /// <summary>
    /// Gets the default decompression command table from the pinned ripgrep revision.
    /// </summary>
    public static ReadOnlyCollection<CliDecompressionCommand> DefaultCommands { get; } = Array.AsReadOnly(
        [
            new CliDecompressionCommand("*.gz", "gzip", "-d", "-c"),
            new CliDecompressionCommand("*.tgz", "gzip", "-d", "-c"),
            new CliDecompressionCommand("*.bz2", "bzip2", "-d", "-c"),
            new CliDecompressionCommand("*.tbz2", "bzip2", "-d", "-c"),
            new CliDecompressionCommand("*.xz", "xz", "-d", "-c"),
            new CliDecompressionCommand("*.txz", "xz", "-d", "-c"),
            new CliDecompressionCommand("*.lz4", "lz4", "-d", "-c"),
            new CliDecompressionCommand("*.lzma", "xz", "--format=lzma", "-d", "-c"),
            new CliDecompressionCommand("*.br", "brotli", "-d", "-c"),
            new CliDecompressionCommand("*.zst", "zstd", "-q", "-d", "-c"),
            new CliDecompressionCommand("*.zstd", "zstd", "-q", "-d", "-c"),
            new CliDecompressionCommand("*.Z", "uncompress", "-c"),
        ]);

    /// <summary>
    /// Tries to find a default decompression command for the given path.
    /// </summary>
    /// <param name="path">The path to match.</param>
    /// <param name="command">The matching decompression command, when one exists.</param>
    /// <returns><see langword="true" /> when a matching command was found.</returns>
    public static bool TryGetDefaultCommand(string path, out CliDecompressionCommand? command)
    {
        ArgumentNullException.ThrowIfNull(path);

        for (int index = DefaultCommands.Count - 1; index >= 0; index--)
        {
            CliDecompressionCommand candidate = DefaultCommands[index];
            if (MatchesGlob(path, candidate.Glob))
            {
                command = candidate;
                return true;
            }
        }

        command = null;
        return false;
    }

    /// <summary>
    /// Tries to find an available default decompression command for the given path.
    /// </summary>
    /// <param name="path">The path to match.</param>
    /// <param name="command">The matching decompression command, when one exists and its program is available.</param>
    /// <returns><see langword="true" /> when a matching available command was found.</returns>
    public static bool TryGetAvailableDefaultCommand(string path, out CliDecompressionCommand? command)
    {
        if (TryGetDefaultCommand(path, out command) &&
            command is not null &&
            TryResolveBinary(command.Program, out string resolvedProgram))
        {
            command = command.WithProgram(resolvedProgram);
            return true;
        }

        command = null;
        return false;
    }

    private static bool MatchesGlob(string path, string glob)
    {
        return glob.Length > 1 &&
            glob[0] == '*' &&
            path.EndsWith(glob[1..], StringComparison.Ordinal);
    }

    internal static bool TryResolveBinary(string program, out string resolvedProgram)
    {
        return TryResolveBinary(
            program,
            ProcessEnvironment.GetVariable("PATH"),
            OperatingSystem.IsWindows(),
            Path.PathSeparator,
            File.Exists,
            out resolvedProgram);
    }

    internal static bool TryResolveBinary(
        string program,
        string? pathVariable,
        bool isWindows,
        char pathSeparator,
        Func<string, bool> fileExists,
        out string resolvedProgram)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (!isWindows || Path.IsPathRooted(program))
        {
            resolvedProgram = program;
            return true;
        }

        if (string.IsNullOrEmpty(pathVariable))
        {
            resolvedProgram = string.Empty;
            return false;
        }

        string[] paths = pathVariable.Split(pathSeparator);
        for (int index = 0; index < paths.Length; index++)
        {
            if (paths[index].Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(paths[index], program);
            if (fileExists(candidate))
            {
                resolvedProgram = candidate;
                return true;
            }

            if (Path.HasExtension(candidate))
            {
                continue;
            }

            string comCandidate = Path.ChangeExtension(candidate, ".com");
            if (fileExists(comCandidate))
            {
                resolvedProgram = comCandidate;
                return true;
            }

            string exeCandidate = Path.ChangeExtension(candidate, ".exe");
            if (fileExists(exeCandidate))
            {
                resolvedProgram = exeCandidate;
                return true;
            }
        }

        resolvedProgram = string.Empty;
        return false;
    }
}
