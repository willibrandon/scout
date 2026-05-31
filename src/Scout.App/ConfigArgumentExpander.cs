using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scout;

internal static class ConfigArgumentExpander
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static OsString[]? BuildConfiguredArguments(
        ReadOnlySpan<OsString> arguments,
        DiagnosticMessenger diagnostics,
        DiagnosticLogger logger,
        string? configPathOverride,
        bool useConfigPathOverride)
    {
        ReadOnlySpan<OsString> commandArguments = arguments[1..];
        if (HasSpecialArgument(commandArguments))
        {
            return null;
        }

        if (HasNoConfigArgument(commandArguments))
        {
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 89, "not reading config files because --no-config is present");
            return null;
        }

        string? configPath = useConfigPathOverride
            ? configPathOverride
            : ProcessEnvironment.GetVariable("RIPGREP_CONFIG_PATH");
        if (string.IsNullOrEmpty(configPath))
        {
            logger.Debug("rg::flags::config", "crates/core/flags/config.rs", 19, "RIPGREP_CONFIG_PATH environment variable is not set, therefore not reading any config file");
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 97, "no extra arguments found from configuration file");
            return null;
        }

        List<OsString> configArguments = ReadConfigArguments(configPath, diagnostics);
        if (configArguments.Count == 0)
        {
            logger.Debug("rg::flags::parse", "crates/core/flags/parse.rs", 97, "no extra arguments found from configuration file");
            return null;
        }

        var expanded = new OsString[configArguments.Count + commandArguments.Length];
        for (int index = 0; index < configArguments.Count; index++)
        {
            expanded[index] = configArguments[index];
        }

        for (int index = 0; index < commandArguments.Length; index++)
        {
            expanded[configArguments.Count + index] = commandArguments[index];
        }

        return expanded;
    }

    public static CliLoggingMode? DetectLoggingMode(ReadOnlySpan<OsString> arguments)
    {
        CliLoggingMode? mode = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            OsString argument = arguments[index];
            if (argument.EqualsUnixBytes("--debug"u8) || TextEquals(argument, "--debug"))
            {
                mode = CliLoggingMode.Debug;
            }
            else if (argument.EqualsUnixBytes("--trace"u8) || TextEquals(argument, "--trace"))
            {
                mode = CliLoggingMode.Trace;
            }
        }

        return mode;
    }

    private static bool HasNoConfigArgument(ReadOnlySpan<OsString> arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            OsString argument = arguments[index];
            if (argument.EqualsUnixBytes("--no-config"u8) ||
                TextEquals(argument, "--no-config"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSpecialArgument(ReadOnlySpan<OsString> arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (IsSpecialArgument(arguments[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSpecialArgument(OsString argument)
    {
        return argument.EqualsUnixBytes("-h"u8) ||
            TextEquals(argument, "-h") ||
            argument.EqualsUnixBytes("--help"u8) ||
            TextEquals(argument, "--help") ||
            argument.EqualsUnixBytes("-V"u8) ||
            TextEquals(argument, "-V") ||
            argument.EqualsUnixBytes("--version"u8) ||
            TextEquals(argument, "--version") ||
            argument.EqualsUnixBytes("--pcre2-version"u8) ||
            TextEquals(argument, "--pcre2-version");
    }

    private static List<OsString> ReadConfigArguments(string configPath, DiagnosticMessenger diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(configPath);
        }
        catch (FileNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {configPath}: No such file or directory (os error 2)").WithContext("rg"));
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {configPath}: No such file or directory (os error 2)").WithContext("rg"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {exception.Message}").WithContext("rg"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.ErrorMessage(new ScoutError(
                $"failed to read the file specified in RIPGREP_CONFIG_PATH: {exception.Message}").WithContext("rg"));
            return [];
        }

        var configArguments = new List<OsString>();
        int lineStart = 0;
        while (lineStart < bytes.Length)
        {
            ReadOnlySpan<byte> remaining = bytes.AsSpan(lineStart);
            int lineFeed = remaining.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line;
            if (lineFeed < 0)
            {
                line = remaining;
                lineStart = bytes.Length;
            }
            else
            {
                line = remaining[..lineFeed];
                lineStart += lineFeed + 1;
            }

            line = TrimAsciiWhitespace(line);
            if (line.IsEmpty || line[0] == (byte)'#')
            {
                continue;
            }

            configArguments.Add(OperatingSystem.IsWindows()
                ? OsString.FromText(Utf8.GetString(line))
                : OsString.FromUnixBytes(line));
        }

        return configArguments;
    }

    private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> bytes)
    {
        int start = 0;
        int end = bytes.Length;
        while (start < end && IsAsciiWhitespace(bytes[start]))
        {
            start++;
        }

        while (end > start && IsAsciiWhitespace(bytes[end - 1]))
        {
            end--;
        }

        return bytes[start..end];
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
    }

    private static bool TextEquals(OsString argument, string expected)
    {
        return argument.TryGetText(out string text) && string.Equals(text, expected, StringComparison.Ordinal);
    }
}
