using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Parses raw operating-system arguments into Scout's low-level CLI representation.
/// </summary>
public static class CliParser
{
    /// <summary>
    /// Parses arguments that do not include the executable name.
    /// </summary>
    /// <param name="arguments">The raw command-line arguments.</param>
    /// <returns>The parse result.</returns>
    public static CliParseResult Parse(ReadOnlySpan<OsString> arguments)
    {
        var lowArgs = new CliLowArgs();

        for (int index = 0; index < arguments.Length; index++)
        {
            OsString argument = arguments[index];
            CliParseResult? special = TryParseSpecial(argument);
            if (special is not null)
            {
                return special;
            }

            if (TryParseGeneratedSwitchFlag(argument, lowArgs, out ScoutError? generatedSwitchError))
            {
                if (generatedSwitchError is not null)
                {
                    return CliParseResult.Fail(generatedSwitchError);
                }

                continue;
            }

            if (TryParseShortFlagCluster(arguments, ref index, lowArgs, out CliParseResult? clusterResult))
            {
                if (clusterResult is not null)
                {
                    return clusterResult;
                }

                continue;
            }

            if (TryParseGeneratedValueFlag(arguments, ref index, lowArgs, out ScoutError? generatedValueError))
            {
                if (generatedValueError is not null)
                {
                    return CliParseResult.Fail(generatedValueError);
                }

                continue;
            }

            if (TryParseValueFlag(arguments, ref index, lowArgs, out ScoutError? valueError))
            {
                if (valueError is not null)
                {
                    return CliParseResult.Fail(valueError);
                }

                continue;
            }

            if (TryParseUnknownFlag(argument, out ScoutError? error))
            {
                return CliParseResult.Fail(error);
            }

            lowArgs.AddPositional(argument);
        }

        return CliParseResult.Ok(lowArgs);
    }

    private static CliParseResult? TryParseSpecial(OsString argument)
    {
        if (argument.EqualsUnixBytes("-h"u8) || TextEquals(argument, "-h"))
        {
            return CliParseResult.Special(CliSpecialMode.HelpShort);
        }

        if (argument.EqualsUnixBytes("--help"u8) || TextEquals(argument, "--help"))
        {
            return CliParseResult.Special(CliSpecialMode.HelpLong);
        }

        if (argument.EqualsUnixBytes("-V"u8) || TextEquals(argument, "-V"))
        {
            return CliParseResult.Special(CliSpecialMode.VersionShort);
        }

        if (argument.EqualsUnixBytes("--version"u8) || TextEquals(argument, "--version"))
        {
            return CliParseResult.Special(CliSpecialMode.VersionLong);
        }

        if (argument.EqualsUnixBytes("--pcre2-version"u8) || TextEquals(argument, "--pcre2-version"))
        {
            return CliParseResult.Special(CliSpecialMode.Pcre2Version);
        }

        return null;
    }

    private static bool TryParseGeneratedSwitchFlag(OsString argument, CliLowArgs lowArgs, out ScoutError? error)
    {
        error = null;
        if (argument.IsUnixBytes)
        {
            return TryDecodeUtf8(argument.AsUnixBytes(), out string text) &&
                TryParseGeneratedSwitchFlag(text, lowArgs, out error);
        }

        return argument.TryGetText(out string? value) &&
            TryParseGeneratedSwitchFlag(value, lowArgs, out error);
    }

    private static bool TryParseGeneratedSwitchFlag(string argument, CliLowArgs lowArgs, out ScoutError? error)
    {
        error = null;
        if (argument.Length == 2 && argument[0] == '-' && argument[1] != '-')
        {
            return GeneratedFlagCatalog.TryFindShortSwitch(argument[1], out FlagDescriptor descriptor) &&
                descriptor.TryApplySwitch(lowArgs, argument, out error);
        }

        if (argument.Length > 2 &&
            argument[0] == '-' &&
            argument[1] == '-' &&
            GeneratedFlagCatalog.TryFindLongSwitch(argument, out FlagDescriptor longDescriptor))
        {
            return longDescriptor.TryApplySwitch(lowArgs, argument, out error);
        }

        return false;
    }

    private static bool TryParseGeneratedValueFlag(
        ReadOnlySpan<OsString> arguments,
        ref int index,
        CliLowArgs lowArgs,
        out ScoutError? error)
    {
        OsString argument = arguments[index];
        if (TryGetGeneratedInlineValue(argument, out FlagDescriptor inlineDescriptor, out string inlineName, out OsString inlineValue))
        {
            return inlineDescriptor.TryApplyValue(lowArgs, inlineValue, inlineName, out error);
        }

        if (TryGetGeneratedValueName(argument, out FlagDescriptor descriptor, out string name))
        {
            if (!TryGetFollowingValue(arguments, ref index, name, out OsString value, out error))
            {
                return true;
            }

            return descriptor.TryApplyValue(lowArgs, value, name, out error);
        }

        error = null;
        return false;
    }

    private static bool TryGetGeneratedValueName(OsString argument, out FlagDescriptor descriptor, out string name)
    {
        if (argument.IsUnixBytes)
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (bytes.Length == 2 &&
                bytes[0] == (byte)'-' &&
                bytes[1] != (byte)'-' &&
                GeneratedFlagCatalog.TryFindShortValue((char)bytes[1], out descriptor))
            {
                name = GetShortValueName((char)bytes[1]);
                return true;
            }

            if (bytes.Length > 2 &&
                bytes[0] == (byte)'-' &&
                bytes[1] == (byte)'-' &&
                TryDecodeUtf8(bytes, out string longName) &&
                GeneratedFlagCatalog.TryFindLongValue(longName, out descriptor))
            {
                name = longName;
                return true;
            }

            descriptor = null!;
            name = string.Empty;
            return false;
        }

        if (!argument.TryGetText(out string text))
        {
            descriptor = null!;
            name = string.Empty;
            return false;
        }

        if (text.Length == 2 &&
            text[0] == '-' &&
            text[1] != '-' &&
            GeneratedFlagCatalog.TryFindShortValue(text[1], out descriptor))
        {
            name = GetShortValueName(text[1]);
            return true;
        }

        if (text.Length > 2 &&
            text[0] == '-' &&
            text[1] == '-' &&
            GeneratedFlagCatalog.TryFindLongValue(text, out descriptor))
        {
            name = text;
            return true;
        }

        descriptor = null!;
        name = string.Empty;
        return false;
    }

    private static bool TryGetGeneratedInlineValue(OsString argument, out FlagDescriptor descriptor, out string name, out OsString value)
    {
        if (argument.IsUnixBytes)
        {
            return TryGetGeneratedInlineUnixValue(argument.AsUnixBytes(), out descriptor, out name, out value);
        }

        if (argument.TryGetText(out string text))
        {
            return TryGetGeneratedInlineTextValue(text, out descriptor, out name, out value);
        }

        descriptor = null!;
        name = string.Empty;
        value = OsString.Empty;
        return false;
    }

    private static bool TryGetGeneratedInlineUnixValue(ReadOnlySpan<byte> bytes, out FlagDescriptor descriptor, out string name, out OsString value)
    {
        if (bytes.Length > 3 && bytes[0] == (byte)'-' && bytes[1] == (byte)'-')
        {
            int equalsIndex = bytes.IndexOf((byte)'=');
            if (equalsIndex > 2 &&
                TryDecodeUtf8(bytes[..equalsIndex], out string longName) &&
                GeneratedFlagCatalog.TryFindLongValue(longName, out descriptor))
            {
                name = longName;
                value = OsString.FromUnixBytes(bytes[(equalsIndex + 1)..]);
                return true;
            }
        }

        if (bytes.Length > 2 &&
            bytes[0] == (byte)'-' &&
            bytes[1] != (byte)'-' &&
            GeneratedFlagCatalog.TryFindShortValue((char)bytes[1], out descriptor))
        {
            ReadOnlySpan<byte> inlineValue = bytes[2..];
            if (!inlineValue.IsEmpty && inlineValue[0] == (byte)'=')
            {
                inlineValue = inlineValue[1..];
            }

            name = GetShortValueName((char)bytes[1]);
            value = OsString.FromUnixBytes(inlineValue);
            return true;
        }

        descriptor = null!;
        name = string.Empty;
        value = OsString.Empty;
        return false;
    }

    private static bool TryGetGeneratedInlineTextValue(string text, out FlagDescriptor descriptor, out string name, out OsString value)
    {
        if (text.Length > 3 && text[0] == '-' && text[1] == '-')
        {
            int equalsIndex = text.IndexOf('=');
            if (equalsIndex > 2)
            {
                string longName = text[..equalsIndex];
                if (GeneratedFlagCatalog.TryFindLongValue(longName, out descriptor))
                {
                    name = longName;
                    value = OsString.FromText(text[(equalsIndex + 1)..]);
                    return true;
                }
            }
        }

        if (text.Length > 2 &&
            text[0] == '-' &&
            text[1] != '-' &&
            GeneratedFlagCatalog.TryFindShortValue(text[1], out descriptor))
        {
            string inlineValue = text[2..];
            if (inlineValue.Length > 0 && inlineValue[0] == '=')
            {
                inlineValue = inlineValue[1..];
            }

            name = GetShortValueName(text[1]);
            value = OsString.FromText(inlineValue);
            return true;
        }

        descriptor = null!;
        name = string.Empty;
        value = OsString.Empty;
        return false;
    }

    private static bool TryParseShortFlagCluster(
        ReadOnlySpan<OsString> arguments,
        ref int index,
        CliLowArgs lowArgs,
        out CliParseResult? result)
    {
        result = null;
        OsString argument = arguments[index];
        if (argument.IsUnixBytes)
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (!IsShortFlagCluster(bytes))
            {
                return false;
            }

            return ParseShortFlagCluster(arguments, ref index, bytes, lowArgs, out result);
        }

        if (!argument.TryGetText(out string text) || !IsShortFlagCluster(text))
        {
            return false;
        }

        return ParseShortFlagCluster(arguments, ref index, text, lowArgs, out result);
    }

    private static bool IsShortFlagCluster(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length > 2 && bytes[0] == (byte)'-' && bytes[1] != (byte)'-';
    }

    private static bool IsShortFlagCluster(string text)
    {
        return text.Length > 2 && text[0] == '-' && text[1] != '-';
    }

    private static bool ParseShortFlagCluster(
        ReadOnlySpan<OsString> arguments,
        ref int argumentIndex,
        ReadOnlySpan<byte> bytes,
        CliLowArgs lowArgs,
        out CliParseResult? result)
    {
        result = null;
        for (int index = 1; index < bytes.Length; index++)
        {
            byte flag = bytes[index];
            if (TryParseClusterSpecial(flag, out CliSpecialMode specialMode))
            {
                result = CliParseResult.Special(specialMode);
                return true;
            }

            if (TryParseClusterSwitch(flag, lowArgs, out ScoutError? switchError))
            {
                if (switchError is not null)
                {
                    result = CliParseResult.Fail(switchError);
                }

                continue;
            }

            if (TryParseClusterValue(arguments, ref argumentIndex, bytes, index, lowArgs, out ScoutError? valueError))
            {
                if (valueError is not null)
                {
                    result = CliParseResult.Fail(valueError);
                }

                return true;
            }

            result = TryDecodeUtf8(bytes.Slice(index, 1), out string name)
                ? CliParseResult.Fail(new ScoutError($"unrecognized flag -{name}"))
                : CliParseResult.Fail(new ScoutError("invalid CLI arguments"));
            return true;
        }

        return true;
    }

    private static bool ParseShortFlagCluster(
        ReadOnlySpan<OsString> arguments,
        ref int argumentIndex,
        string text,
        CliLowArgs lowArgs,
        out CliParseResult? result)
    {
        result = null;
        for (int index = 1; index < text.Length; index++)
        {
            char flag = text[index];
            if (TryParseClusterSpecial(flag, out CliSpecialMode specialMode))
            {
                result = CliParseResult.Special(specialMode);
                return true;
            }

            if (TryParseClusterSwitch(flag, lowArgs, out ScoutError? switchError))
            {
                if (switchError is not null)
                {
                    result = CliParseResult.Fail(switchError);
                }

                continue;
            }

            if (TryParseClusterValue(arguments, ref argumentIndex, text, index, lowArgs, out ScoutError? valueError))
            {
                if (valueError is not null)
                {
                    result = CliParseResult.Fail(valueError);
                }

                return true;
            }

            result = CliParseResult.Fail(new ScoutError($"unrecognized flag -{flag}"));
            return true;
        }

        return true;
    }

    private static bool TryParseClusterSpecial(byte flag, out CliSpecialMode mode)
    {
        return TryParseClusterSpecial((char)flag, out mode);
    }

    private static bool TryParseClusterSpecial(char flag, out CliSpecialMode mode)
    {
        switch (flag)
        {
            case 'h':
                mode = CliSpecialMode.HelpShort;
                return true;

            case 'V':
                mode = CliSpecialMode.VersionShort;
                return true;

            default:
                mode = default;
                return false;
        }
    }

    private static bool TryParseClusterSwitch(byte flag, CliLowArgs lowArgs, out ScoutError? error)
    {
        return TryParseClusterSwitch((char)flag, lowArgs, out error);
    }

    private static bool TryParseClusterSwitch(char flag, CliLowArgs lowArgs, out ScoutError? error)
    {
        error = null;
        if (GeneratedFlagCatalog.TryFindShortSwitch(flag, out FlagDescriptor descriptor))
        {
            return descriptor.TryApplySwitch(lowArgs, GetShortSwitchName(flag), out error);
        }

        return false;
    }

    private static bool TryParseClusterValue(
        ReadOnlySpan<OsString> arguments,
        ref int argumentIndex,
        ReadOnlySpan<byte> bytes,
        int flagIndex,
        CliLowArgs lowArgs,
        out ScoutError? error)
    {
        byte flag = bytes[flagIndex];
        if (!IsClusterValueFlag(flag))
        {
            error = null;
            return false;
        }

        string flagName = GetShortValueName((char)flag);
        ReadOnlySpan<byte> value = bytes[(flagIndex + 1)..];
        if (!value.IsEmpty)
        {
            if (value[0] == (byte)'=')
            {
                value = value[1..];
            }

            return ParseClusterValue(flag, value, flagName, lowArgs, out error);
        }

        if (!TryGetFollowingValue(arguments, ref argumentIndex, flagName, out OsString followingValue, out error))
        {
            return true;
        }

        return ParseClusterValue(flag, followingValue, flagName, lowArgs, out error);
    }

    private static bool TryParseClusterValue(
        ReadOnlySpan<OsString> arguments,
        ref int argumentIndex,
        string text,
        int flagIndex,
        CliLowArgs lowArgs,
        out ScoutError? error)
    {
        char flag = text[flagIndex];
        if (!IsClusterValueFlag(flag))
        {
            error = null;
            return false;
        }

        string flagName = GetShortValueName(flag);
        string value = text[(flagIndex + 1)..];
        if (value.Length > 0)
        {
            if (value[0] == '=')
            {
                value = value[1..];
            }

            return ParseClusterValue(flag, value, flagName, lowArgs, out error);
        }

        if (!TryGetFollowingValue(arguments, ref argumentIndex, flagName, out OsString followingValue, out error))
        {
            return true;
        }

        return ParseClusterValue(flag, followingValue, flagName, lowArgs, out error);
    }

    private static bool IsClusterValueFlag(byte flag)
    {
        return IsClusterValueFlag((char)flag);
    }

    private static bool IsClusterValueFlag(char flag)
    {
        return GeneratedFlagCatalog.TryFindShortValue(flag, out _) ||
            flag is 'r' or 'e' or 'f' or 'g' or 't' or 'T';
    }

    private static string GetShortSwitchName(char flag)
    {
        return flag switch
        {
            '0' => "-0",
            '.' => "-.",
            _ => new string(['-', flag]),
        };
    }

    private static string GetShortFlagName(char flag)
    {
        return flag switch
        {
            'm' => "-m",
            'M' => "-M",
            'j' => "-j",
            'r' => "-r",
            'e' => "-e",
            'f' => "-f",
            'A' => "-A",
            'B' => "-B",
            'C' => "-C",
            'd' => "-d",
            'g' => "-g",
            't' => "-t",
            'T' => "-T",
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };
    }

    private static string GetShortValueName(char flag)
    {
        return GeneratedFlagCatalog.TryFindShortValue(flag, out _)
            ? new string(['-', flag])
            : GetShortFlagName(flag);
    }

    private static bool ParseClusterValue(byte flag, ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (GeneratedFlagCatalog.TryFindShortValue((char)flag, out FlagDescriptor descriptor))
        {
            return descriptor.TryApplyValue(lowArgs, OsString.FromUnixBytes(value), flagName, out error);
        }

        return (char)flag switch
        {
            'm' => ParseMaxCount(value, flagName, lowArgs, out error),
            'M' => ParseMaxColumns(value, flagName, lowArgs, out error),
            'j' => ParseThreads(value, flagName, lowArgs, out error),
            'r' => ParseReplacement(value, lowArgs, out error),
            'e' => ParsePattern(value, lowArgs, out error),
            'f' => ParsePatternFile(value, lowArgs, out error),
            'A' => ParseAfterContext(value, flagName, lowArgs, out error),
            'B' => ParseBeforeContext(value, flagName, lowArgs, out error),
            'C' => ParseContext(value, flagName, lowArgs, out error),
            'd' => ParseMaxDepth(value, flagName, lowArgs, out error),
            'g' => ParseGlob(value, flagName, caseInsensitive: false, lowArgs, out error),
            't' => ParseTypeChange(value, flagName, CliTypeChangeKind.Select, lowArgs, out error),
            'T' => ParseTypeChange(value, flagName, CliTypeChangeKind.Negate, lowArgs, out error),
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };
    }

    private static bool ParseClusterValue(byte flag, OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        return ParseClusterValue((char)flag, value, flagName, lowArgs, out error);
    }

    private static bool ParseClusterValue(char flag, string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (GeneratedFlagCatalog.TryFindShortValue(flag, out FlagDescriptor descriptor))
        {
            return descriptor.TryApplyValue(lowArgs, OsString.FromText(value), flagName, out error);
        }

        return flag switch
        {
            'm' => ParseMaxCount(value, flagName, lowArgs, out error),
            'M' => ParseMaxColumns(value, flagName, lowArgs, out error),
            'j' => ParseThreads(value, flagName, lowArgs, out error),
            'r' => ParseReplacement(value, lowArgs, out error),
            'e' => ParsePattern(value, lowArgs, out error),
            'f' => ParsePatternFile(value, lowArgs, out error),
            'A' => ParseAfterContext(value, flagName, lowArgs, out error),
            'B' => ParseBeforeContext(value, flagName, lowArgs, out error),
            'C' => ParseContext(value, flagName, lowArgs, out error),
            'd' => ParseMaxDepth(value, flagName, lowArgs, out error),
            'g' => ParseGlob(value, caseInsensitive: false, lowArgs, out error),
            't' => ParseTypeChange(value, CliTypeChangeKind.Select, lowArgs, out error),
            'T' => ParseTypeChange(value, CliTypeChangeKind.Negate, lowArgs, out error),
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };
    }

    private static bool ParseClusterValue(char flag, OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (GeneratedFlagCatalog.TryFindShortValue(flag, out FlagDescriptor descriptor))
        {
            return descriptor.TryApplyValue(lowArgs, value, flagName, out error);
        }

        return flag switch
        {
            'm' => ParseMaxCount(value, flagName, lowArgs, out error),
            'M' => ParseMaxColumns(value, flagName, lowArgs, out error),
            'j' => ParseThreads(value, flagName, lowArgs, out error),
            'r' => ParseReplacement(value, lowArgs, out error),
            'e' => ParsePattern(value, lowArgs, out error),
            'f' => ParsePatternFile(value, lowArgs, out error),
            'A' => ParseAfterContext(value, flagName, lowArgs, out error),
            'B' => ParseBeforeContext(value, flagName, lowArgs, out error),
            'C' => ParseContext(value, flagName, lowArgs, out error),
            'd' => ParseMaxDepth(value, flagName, lowArgs, out error),
            'g' => ParseGlob(value, flagName, caseInsensitive: false, lowArgs, out error),
            't' => ParseTypeChange(value, flagName, CliTypeChangeKind.Select, lowArgs, out error),
            'T' => ParseTypeChange(value, flagName, CliTypeChangeKind.Negate, lowArgs, out error),
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };
    }

    private static bool TryParseValueFlag(
        ReadOnlySpan<OsString> arguments,
        ref int index,
        CliLowArgs lowArgs,
        out ScoutError? error)
    {
        OsString argument = arguments[index];
        error = null;

        if (argument.EqualsUnixBytes("--hostname-bin"u8) || TextEquals(argument, "--hostname-bin"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--hostname-bin", out OsString value, out error))
            {
                return true;
            }

            return ParseHostnameBin(value, "--hostname-bin", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--hyperlink-format"u8) || TextEquals(argument, "--hyperlink-format"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--hyperlink-format", out OsString value, out error))
            {
                return true;
            }

            return ParseHyperlinkFormat(value, "--hyperlink-format", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-r"u8) || TextEquals(argument, "-r"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-r", out OsString value, out error))
            {
                return true;
            }

            return ParseReplacement(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--replace"u8) || TextEquals(argument, "--replace"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--replace", out OsString value, out error))
            {
                return true;
            }

            return ParseReplacement(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-e"u8) || TextEquals(argument, "-e"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-e", out OsString value, out error))
            {
                return true;
            }

            return ParsePattern(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--regexp"u8) || TextEquals(argument, "--regexp"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--regexp", out OsString value, out error))
            {
                return true;
            }

            return ParsePattern(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-f"u8) || TextEquals(argument, "-f"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-f", out OsString value, out error))
            {
                return true;
            }

            return ParsePatternFile(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--file"u8) || TextEquals(argument, "--file"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--file", out OsString value, out error))
            {
                return true;
            }

            return ParsePatternFile(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--field-match-separator"u8) || TextEquals(argument, "--field-match-separator"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--field-match-separator", out OsString value, out error))
            {
                return true;
            }

            return ParseSeparator(value, "--field-match-separator", SeparatorKind.FieldMatch, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--field-context-separator"u8) || TextEquals(argument, "--field-context-separator"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--field-context-separator", out OsString value, out error))
            {
                return true;
            }

            return ParseSeparator(value, "--field-context-separator", SeparatorKind.FieldContext, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--context-separator"u8) || TextEquals(argument, "--context-separator"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--context-separator", out OsString value, out error))
            {
                return true;
            }

            return ParseSeparator(value, "--context-separator", SeparatorKind.Context, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--path-separator"u8) || TextEquals(argument, "--path-separator"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--path-separator", out OsString value, out error))
            {
                return true;
            }

            return ParsePathSeparator(value, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--max-filesize"u8) || TextEquals(argument, "--max-filesize"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--max-filesize", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxFileSize(value, "--max-filesize", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-g"u8) || TextEquals(argument, "-g"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-g", out OsString value, out error))
            {
                return true;
            }

            return ParseGlob(value, "-g", caseInsensitive: false, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--glob"u8) || TextEquals(argument, "--glob"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--glob", out OsString value, out error))
            {
                return true;
            }

            return ParseGlob(value, "--glob", caseInsensitive: false, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--iglob"u8) || TextEquals(argument, "--iglob"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--iglob", out OsString value, out error))
            {
                return true;
            }

            return ParseGlob(value, "--iglob", caseInsensitive: true, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--ignore-file"u8) || TextEquals(argument, "--ignore-file"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--ignore-file", out OsString value, out error))
            {
                return true;
            }

            return ParseIgnoreFile(value, "--ignore-file", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--pre"u8) || TextEquals(argument, "--pre"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--pre", out OsString value, out error))
            {
                return true;
            }

            return ParsePreprocessor(value, "--pre", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--pre-glob"u8) || TextEquals(argument, "--pre-glob"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--pre-glob", out OsString value, out error))
            {
                return true;
            }

            return ParsePreprocessorGlob(value, "--pre-glob", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--sort"u8) || TextEquals(argument, "--sort"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--sort", out OsString value, out error))
            {
                return true;
            }

            return ParseSort(value, "--sort", reverse: false, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--sortr"u8) || TextEquals(argument, "--sortr"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--sortr", out OsString value, out error))
            {
                return true;
            }

            return ParseSort(value, "--sortr", reverse: true, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-t"u8) || TextEquals(argument, "-t"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-t", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "-t", CliTypeChangeKind.Select, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--type"u8) || TextEquals(argument, "--type"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--type", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "--type", CliTypeChangeKind.Select, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-T"u8) || TextEquals(argument, "-T"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-T", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "-T", CliTypeChangeKind.Negate, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--type-not"u8) || TextEquals(argument, "--type-not"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--type-not", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "--type-not", CliTypeChangeKind.Negate, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--type-add"u8) || TextEquals(argument, "--type-add"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--type-add", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "--type-add", CliTypeChangeKind.Add, lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--type-clear"u8) || TextEquals(argument, "--type-clear"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--type-clear", out OsString value, out error))
            {
                return true;
            }

            return ParseTypeChange(value, "--type-clear", CliTypeChangeKind.Clear, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--hostname-bin="u8, out ReadOnlySpan<byte> hostnameBinValue))
        {
            return ParseHostnameBin(hostnameBinValue, "--hostname-bin", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--hyperlink-format="u8, out ReadOnlySpan<byte> hyperlinkFormatValue))
        {
            return ParseHyperlinkFormat(hyperlinkFormatValue, "--hyperlink-format", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--replace="u8, out ReadOnlySpan<byte> replacementValue))
        {
            return ParseReplacement(replacementValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--regexp="u8, out ReadOnlySpan<byte> patternValue))
        {
            return ParsePattern(patternValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--file="u8, out ReadOnlySpan<byte> patternFileValue))
        {
            return ParsePatternFile(patternFileValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--field-match-separator="u8, out ReadOnlySpan<byte> fieldMatchSeparator))
        {
            return ParseSeparator(fieldMatchSeparator, "--field-match-separator", SeparatorKind.FieldMatch, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--field-context-separator="u8, out ReadOnlySpan<byte> fieldContextSeparator))
        {
            return ParseSeparator(fieldContextSeparator, "--field-context-separator", SeparatorKind.FieldContext, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--context-separator="u8, out ReadOnlySpan<byte> contextSeparator))
        {
            return ParseSeparator(contextSeparator, "--context-separator", SeparatorKind.Context, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--path-separator="u8, out ReadOnlySpan<byte> pathSeparator))
        {
            return ParsePathSeparator(pathSeparator, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-r"u8, out ReadOnlySpan<byte> shortReplacementValue))
        {
            if (!shortReplacementValue.IsEmpty && shortReplacementValue[0] == (byte)'=')
            {
                shortReplacementValue = shortReplacementValue[1..];
            }

            return ParseReplacement(shortReplacementValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-e"u8, out ReadOnlySpan<byte> shortPatternValue))
        {
            if (!shortPatternValue.IsEmpty && shortPatternValue[0] == (byte)'=')
            {
                shortPatternValue = shortPatternValue[1..];
            }

            return ParsePattern(shortPatternValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-f"u8, out ReadOnlySpan<byte> shortPatternFileValue))
        {
            if (!shortPatternFileValue.IsEmpty && shortPatternFileValue[0] == (byte)'=')
            {
                shortPatternFileValue = shortPatternFileValue[1..];
            }

            return ParsePatternFile(shortPatternFileValue, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--max-filesize="u8, out ReadOnlySpan<byte> maxFileSizeValue))
        {
            return ParseMaxFileSize(maxFileSizeValue, "--max-filesize", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--glob="u8, out ReadOnlySpan<byte> longGlobValue))
        {
            return ParseGlob(longGlobValue, "--glob", caseInsensitive: false, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--iglob="u8, out ReadOnlySpan<byte> longInsensitiveGlobValue))
        {
            return ParseGlob(longInsensitiveGlobValue, "--iglob", caseInsensitive: true, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-g"u8, out ReadOnlySpan<byte> shortGlobValue))
        {
            if (!shortGlobValue.IsEmpty && shortGlobValue[0] == (byte)'=')
            {
                shortGlobValue = shortGlobValue[1..];
            }

            return ParseGlob(shortGlobValue, "-g", caseInsensitive: false, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--ignore-file="u8, out ReadOnlySpan<byte> ignoreFileValue))
        {
            return ParseIgnoreFile(ignoreFileValue, "--ignore-file", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--pre="u8, out ReadOnlySpan<byte> preprocessorValue))
        {
            return ParsePreprocessor(preprocessorValue, "--pre", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--pre-glob="u8, out ReadOnlySpan<byte> preprocessorGlobValue))
        {
            return ParsePreprocessorGlob(preprocessorGlobValue, "--pre-glob", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--sort="u8, out ReadOnlySpan<byte> sortValue))
        {
            return ParseSort(sortValue, "--sort", reverse: false, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--sortr="u8, out ReadOnlySpan<byte> sortReverseValue))
        {
            return ParseSort(sortReverseValue, "--sortr", reverse: true, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--type="u8, out ReadOnlySpan<byte> typeValue))
        {
            return ParseTypeChange(typeValue, "--type", CliTypeChangeKind.Select, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-t"u8, out ReadOnlySpan<byte> shortTypeValue))
        {
            return ParseTypeChange(shortTypeValue, "-t", CliTypeChangeKind.Select, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--type-not="u8, out ReadOnlySpan<byte> typeNotValue))
        {
            return ParseTypeChange(typeNotValue, "--type-not", CliTypeChangeKind.Negate, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-T"u8, out ReadOnlySpan<byte> shortTypeNotValue))
        {
            return ParseTypeChange(shortTypeNotValue, "-T", CliTypeChangeKind.Negate, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--type-add="u8, out ReadOnlySpan<byte> typeAddValue))
        {
            return ParseTypeChange(typeAddValue, "--type-add", CliTypeChangeKind.Add, lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--type-clear="u8, out ReadOnlySpan<byte> typeClearValue))
        {
            return ParseTypeChange(typeClearValue, "--type-clear", CliTypeChangeKind.Clear, lowArgs, out error);
        }

        if (argument.TryGetText(out string text))
        {
            if (text.StartsWith("--hostname-bin=", StringComparison.Ordinal))
            {
                return ParseHostnameBin(text["--hostname-bin=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--hyperlink-format=", StringComparison.Ordinal))
            {
                return ParseHyperlinkFormat(text["--hyperlink-format=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--replace=", StringComparison.Ordinal))
            {
                return ParseReplacement(text["--replace=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--regexp=", StringComparison.Ordinal))
            {
                return ParsePattern(text["--regexp=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--file=", StringComparison.Ordinal))
            {
                return ParsePatternFile(text["--file=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--field-match-separator=", StringComparison.Ordinal))
            {
                return ParseSeparator(text["--field-match-separator=".Length..], SeparatorKind.FieldMatch, lowArgs, out error);
            }

            if (text.StartsWith("--field-context-separator=", StringComparison.Ordinal))
            {
                return ParseSeparator(text["--field-context-separator=".Length..], SeparatorKind.FieldContext, lowArgs, out error);
            }

            if (text.StartsWith("--context-separator=", StringComparison.Ordinal))
            {
                return ParseSeparator(text["--context-separator=".Length..], SeparatorKind.Context, lowArgs, out error);
            }

            if (text.StartsWith("--path-separator=", StringComparison.Ordinal))
            {
                return ParsePathSeparator(text["--path-separator=".Length..], lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-r", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseReplacement(value, lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-e", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParsePattern(value, lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-f", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParsePatternFile(value, lowArgs, out error);
            }

            if (text.StartsWith("--max-filesize=", StringComparison.Ordinal))
            {
                return ParseMaxFileSize(text["--max-filesize=".Length..], "--max-filesize", lowArgs, out error);
            }

            if (text.StartsWith("--glob=", StringComparison.Ordinal))
            {
                return ParseGlob(text["--glob=".Length..], caseInsensitive: false, lowArgs, out error);
            }

            if (text.StartsWith("--iglob=", StringComparison.Ordinal))
            {
                return ParseGlob(text["--iglob=".Length..], caseInsensitive: true, lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-g", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseGlob(value, caseInsensitive: false, lowArgs, out error);
            }

            if (text.StartsWith("--ignore-file=", StringComparison.Ordinal))
            {
                return ParseIgnoreFile(text["--ignore-file=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--pre=", StringComparison.Ordinal))
            {
                return ParsePreprocessor(text["--pre=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--pre-glob=", StringComparison.Ordinal))
            {
                return ParsePreprocessorGlob(text["--pre-glob=".Length..], lowArgs, out error);
            }

            if (text.StartsWith("--sort=", StringComparison.Ordinal))
            {
                return ParseSort(text["--sort=".Length..], "--sort", reverse: false, lowArgs, out error);
            }

            if (text.StartsWith("--sortr=", StringComparison.Ordinal))
            {
                return ParseSort(text["--sortr=".Length..], "--sortr", reverse: true, lowArgs, out error);
            }

            if (text.StartsWith("--type=", StringComparison.Ordinal))
            {
                return ParseTypeChange(text["--type=".Length..], CliTypeChangeKind.Select, lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-t", StringComparison.Ordinal))
            {
                return ParseTypeChange(text[2..], CliTypeChangeKind.Select, lowArgs, out error);
            }

            if (text.StartsWith("--type-not=", StringComparison.Ordinal))
            {
                return ParseTypeChange(text["--type-not=".Length..], CliTypeChangeKind.Negate, lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-T", StringComparison.Ordinal))
            {
                return ParseTypeChange(text[2..], CliTypeChangeKind.Negate, lowArgs, out error);
            }

            if (text.StartsWith("--type-add=", StringComparison.Ordinal))
            {
                return ParseTypeChange(text["--type-add=".Length..], CliTypeChangeKind.Add, lowArgs, out error);
            }

            if (text.StartsWith("--type-clear=", StringComparison.Ordinal))
            {
                return ParseTypeChange(text["--type-clear=".Length..], CliTypeChangeKind.Clear, lowArgs, out error);
            }
        }

        return false;
    }

    private static bool TryGetFollowingValue(
        ReadOnlySpan<OsString> arguments,
        ref int index,
        string flagName,
        out OsString value,
        out ScoutError? error)
    {
        if (index + 1 >= arguments.Length)
        {
            value = OsString.Empty;
            error = new ScoutError($"missing value for flag {flagName}: missing argument for option '{flagName}'");
            return false;
        }

        index++;
        value = arguments[index];
        error = null;
        return true;
    }

    private static bool TryGetInlineUnixValue(OsString argument, ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> value)
    {
        if (argument.IsUnixBytes)
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (bytes.Length > prefix.Length && bytes.StartsWith(prefix))
            {
                value = bytes[prefix.Length..];
                return true;
            }
        }

        value = [];
        return false;
    }

    internal static bool ParseMaxCount(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseMaxCount(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseMaxCount(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseMaxCount(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong count, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxCount(count);
        error = null;
        return true;
    }

    internal static bool ParseMaxColumns(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseMaxColumns(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseMaxColumns(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseMaxColumns(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong columns, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxColumns(columns);
        error = null;
        return true;
    }

    internal static bool ParseThreads(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseThreads(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseThreads(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseThreads(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong threads, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetThreads(threads);
        error = null;
        return true;
    }

    internal static bool ParseRegexEngine(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseRegexEngine(text, lowArgs, out error);
        }

        error = new ScoutError("error parsing flag --engine: invalid UTF-8 in regex engine");
        return true;
    }

    private static bool ParseRegexEngine(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        switch (value)
        {
            case "default":
                lowArgs.SetRegexEngine(CliRegexEngine.Default);
                error = null;
                return true;

            case "auto":
                lowArgs.SetRegexEngine(CliRegexEngine.Auto);
                error = null;
                return true;

            case "pcre2":
                lowArgs.SetRegexEngine(CliRegexEngine.Pcre2);
                error = null;
                return true;

            default:
                error = new ScoutError($"error parsing flag --engine: unrecognized regex engine '{value}'");
                return true;
        }
    }

    internal static bool ParseEncoding(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseEncoding(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseEncoding(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseEncoding(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryDecodeUtf8(value, out string text))
        {
            error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in encoding");
            return true;
        }

        return ParseEncoding(text, flagName, lowArgs, out error);
    }

    private static bool ParseEncoding(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        switch (value)
        {
            case "auto":
                lowArgs.SetEncodingMode(CliEncodingMode.Auto);
                error = null;
                return true;

            case "none":
                lowArgs.SetEncodingMode(CliEncodingMode.None);
                error = null;
                return true;
        }

        if (SearchEncodingLabel.TryGetKind(value, out SearchEncodingKind encodingKind))
        {
            lowArgs.SetEncodingMode(ToCliEncodingMode(encodingKind));
            error = null;
            return true;
        }

        error = new ScoutError($"error parsing flag {flagName}: grep config error: unknown encoding: {value}");
        return true;
    }

    private static CliEncodingMode ToCliEncodingMode(SearchEncodingKind encodingKind)
    {
        return encodingKind switch
        {
            SearchEncodingKind.Utf8 => CliEncodingMode.Utf8,
            SearchEncodingKind.Utf16 => CliEncodingMode.Utf16,
            SearchEncodingKind.Utf16Le => CliEncodingMode.Utf16Le,
            SearchEncodingKind.Utf16Be => CliEncodingMode.Utf16Be,
            SearchEncodingKind.EucKr => CliEncodingMode.EucKr,
            SearchEncodingKind.EucJp => CliEncodingMode.EucJp,
            SearchEncodingKind.Big5 => CliEncodingMode.Big5,
            SearchEncodingKind.Gb18030 => CliEncodingMode.Gb18030,
            SearchEncodingKind.Gbk => CliEncodingMode.Gbk,
            SearchEncodingKind.ShiftJis => CliEncodingMode.ShiftJis,
            SearchEncodingKind.Ibm866 => CliEncodingMode.Ibm866,
            SearchEncodingKind.Iso88592 => CliEncodingMode.Iso88592,
            SearchEncodingKind.Iso88593 => CliEncodingMode.Iso88593,
            SearchEncodingKind.Iso88594 => CliEncodingMode.Iso88594,
            SearchEncodingKind.Iso88595 => CliEncodingMode.Iso88595,
            SearchEncodingKind.Iso88596 => CliEncodingMode.Iso88596,
            SearchEncodingKind.Iso88597 => CliEncodingMode.Iso88597,
            SearchEncodingKind.Iso88598 => CliEncodingMode.Iso88598,
            SearchEncodingKind.Iso88598I => CliEncodingMode.Iso88598I,
            SearchEncodingKind.Iso885910 => CliEncodingMode.Iso885910,
            SearchEncodingKind.Iso885913 => CliEncodingMode.Iso885913,
            SearchEncodingKind.Iso885914 => CliEncodingMode.Iso885914,
            SearchEncodingKind.Iso885915 => CliEncodingMode.Iso885915,
            SearchEncodingKind.Iso885916 => CliEncodingMode.Iso885916,
            SearchEncodingKind.Iso2022Jp => CliEncodingMode.Iso2022Jp,
            SearchEncodingKind.Koi8R => CliEncodingMode.Koi8R,
            SearchEncodingKind.Koi8U => CliEncodingMode.Koi8U,
            SearchEncodingKind.Macintosh => CliEncodingMode.Macintosh,
            SearchEncodingKind.Windows874 => CliEncodingMode.Windows874,
            SearchEncodingKind.Windows1250 => CliEncodingMode.Windows1250,
            SearchEncodingKind.Windows1251 => CliEncodingMode.Windows1251,
            SearchEncodingKind.Windows1252 => CliEncodingMode.Windows1252,
            SearchEncodingKind.Windows1253 => CliEncodingMode.Windows1253,
            SearchEncodingKind.Windows1254 => CliEncodingMode.Windows1254,
            SearchEncodingKind.Windows1255 => CliEncodingMode.Windows1255,
            SearchEncodingKind.Windows1256 => CliEncodingMode.Windows1256,
            SearchEncodingKind.Windows1257 => CliEncodingMode.Windows1257,
            SearchEncodingKind.Windows1258 => CliEncodingMode.Windows1258,
            SearchEncodingKind.XMacCyrillic => CliEncodingMode.XMacCyrillic,
            SearchEncodingKind.XUserDefined => CliEncodingMode.XUserDefined,
            _ => throw new ArgumentOutOfRangeException(nameof(encodingKind), encodingKind, "Unsupported search encoding kind."),
        };
    }

    internal static bool ParseColor(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseColor(value.AsUnixBytes(), lowArgs, out error);
        }

        return ParseColor(value.AsWindowsString(), lowArgs, out error);
    }

    private static bool ParseColor(ReadOnlySpan<byte> value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.SequenceEqual("auto"u8))
        {
            lowArgs.SetColorMode(CliColorMode.Auto);
            error = null;
            return true;
        }

        if (value.SequenceEqual("always"u8))
        {
            lowArgs.SetColorMode(CliColorMode.Always);
            error = null;
            return true;
        }

        if (value.SequenceEqual("ansi"u8))
        {
            lowArgs.SetColorMode(CliColorMode.Ansi);
            error = null;
            return true;
        }

        if (value.SequenceEqual("never"u8))
        {
            lowArgs.SetColorMode(CliColorMode.Never);
            error = null;
            return true;
        }

        string choice = Utf8.GetString(value);
        error = new ScoutError($"error parsing flag --color: choice '{choice}' is unrecognized");
        return true;
    }

    private static bool ParseReplacement(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseReplacement(value.AsUnixBytes(), lowArgs, out error);
        }

        return ParseReplacement(value.AsWindowsString(), lowArgs, out error);
    }

    private static bool ParseReplacement(ReadOnlySpan<byte> value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.SetReplacement(value);
        error = null;
        return true;
    }

    private static bool ParsePattern(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPattern(value);
        error = null;
        return true;
    }

    private static bool ParsePattern(ReadOnlySpan<byte> value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPattern(OsString.FromUnixBytes(value));
        error = null;
        return true;
    }

    private static bool ParsePattern(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPattern(OsString.FromText(value));
        error = null;
        return true;
    }

    private static bool ParsePatternFile(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPatternFile(value);
        error = null;
        return true;
    }

    private static bool ParsePatternFile(ReadOnlySpan<byte> value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPatternFile(OsString.FromUnixBytes(value));
        error = null;
        return true;
    }

    private static bool ParsePatternFile(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPatternFile(OsString.FromText(value));
        error = null;
        return true;
    }

    internal static bool ParseAfterContext(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetAfterContext(count);
        return true;
    }

    private static bool ParseAfterContext(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetAfterContext(count);
        return true;
    }

    private static bool ParseAfterContext(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetAfterContext(count);
        return true;
    }

    internal static bool ParseBeforeContext(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetBeforeContext(count);
        return true;
    }

    private static bool ParseBeforeContext(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetBeforeContext(count);
        return true;
    }

    private static bool ParseBeforeContext(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetBeforeContext(count);
        return true;
    }

    internal static bool ParseContext(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetContext(count);
        return true;
    }

    private static bool ParseContext(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetContext(count);
        return true;
    }

    private static bool ParseContext(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseContext(value, flagName, out ulong count, out error))
        {
            return true;
        }

        lowArgs.SetContext(count);
        return true;
    }

    private static bool TryParseContext(OsString value, string flagName, out ulong count, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return TryParseContext(value.AsUnixBytes(), flagName, out count, out error);
        }

        return TryParseContext(value.AsWindowsString(), flagName, out count, out error);
    }

    private static bool TryParseContext(ReadOnlySpan<byte> value, string flagName, out ulong count, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out count, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseContext(string value, string flagName, out ulong count, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out count, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return false;
        }

        error = null;
        return true;
    }

    internal static bool ParseMaxDepth(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseMaxDepth(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseMaxDepth(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseMaxDepth(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong depth, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxDepth(depth);
        error = null;
        return true;
    }

    private static bool ParseMaxDepth(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong depth, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxDepth(depth);
        error = null;
        return true;
    }

    private static bool ParseMaxFileSize(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseMaxFileSize(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseMaxFileSize(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseMaxFileSize(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong bytes, out error))
        {
            return true;
        }

        lowArgs.SetMaxFileSize(bytes);
        error = null;
        return true;
    }

    private static bool ParseMaxFileSize(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong size, out error))
        {
            return true;
        }

        lowArgs.SetMaxFileSize(size);
        error = null;
        return true;
    }

    private static bool ParseMaxCount(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong count, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxCount(count);
        error = null;
        return true;
    }

    private static bool ParseMaxColumns(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong columns, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetMaxColumns(columns);
        error = null;
        return true;
    }

    private static bool ParseThreads(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseUnsigned(value, out ulong threads, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: value is not a valid number: {parseError}");
            return true;
        }

        lowArgs.SetThreads(threads);
        error = null;
        return true;
    }

    private static bool ParseReplacement(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.SetReplacement(Utf8.GetBytes(value));
        error = null;
        return true;
    }

    private static bool ParseColor(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (string.Equals(value, "auto", StringComparison.Ordinal))
        {
            lowArgs.SetColorMode(CliColorMode.Auto);
            error = null;
            return true;
        }

        if (string.Equals(value, "always", StringComparison.Ordinal))
        {
            lowArgs.SetColorMode(CliColorMode.Always);
            error = null;
            return true;
        }

        if (string.Equals(value, "ansi", StringComparison.Ordinal))
        {
            lowArgs.SetColorMode(CliColorMode.Ansi);
            error = null;
            return true;
        }

        if (string.Equals(value, "never", StringComparison.Ordinal))
        {
            lowArgs.SetColorMode(CliColorMode.Never);
            error = null;
            return true;
        }

        error = new ScoutError($"error parsing flag --color: choice '{value}' is unrecognized");
        return true;
    }

    internal static bool ParseGenerate(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseGenerate(text, lowArgs, out error);
        }

        error = new ScoutError("error parsing flag --generate: invalid UTF-8 in generate mode");
        return true;
    }

    private static bool ParseGenerate(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        switch (value)
        {
            case "man":
                lowArgs.SetGenerateMode(CliGenerateMode.Man);
                error = null;
                return true;

            case "complete-bash":
                lowArgs.SetGenerateMode(CliGenerateMode.CompleteBash);
                error = null;
                return true;

            case "complete-zsh":
                lowArgs.SetGenerateMode(CliGenerateMode.CompleteZsh);
                error = null;
                return true;

            case "complete-fish":
                lowArgs.SetGenerateMode(CliGenerateMode.CompleteFish);
                error = null;
                return true;

            case "complete-powershell":
                lowArgs.SetGenerateMode(CliGenerateMode.CompletePowerShell);
                error = null;
                return true;

            default:
                error = new ScoutError($"error parsing flag --generate: choice '{value}' is unrecognized");
                return true;
        }
    }

    internal static bool ParseColorSpec(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseColorSpec(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in color specification");
        return true;
    }

    private static bool ParseColorSpec(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!CliColorSpecParser.TryParse(value, out _, out _, out _, out _, out _, out _, out _, out string? parseError))
        {
            error = new ScoutError($"error parsing flag --colors: {parseError}");
            return true;
        }

        lowArgs.AddColorSpec(value);
        error = null;
        return true;
    }

    internal static bool ParseDfaSizeLimit(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseDfaSizeLimit(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseDfaSizeLimit(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseDfaSizeLimit(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong bytes, out error))
        {
            return true;
        }

        lowArgs.SetDfaSizeLimit(bytes);
        error = null;
        return true;
    }

    private static bool ParseDfaSizeLimit(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong size, out error))
        {
            return true;
        }

        lowArgs.SetDfaSizeLimit(size);
        error = null;
        return true;
    }

    internal static bool ParseRegexSizeLimit(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseRegexSizeLimit(value.AsUnixBytes(), flagName, lowArgs, out error);
        }

        return ParseRegexSizeLimit(value.AsWindowsString(), flagName, lowArgs, out error);
    }

    private static bool ParseRegexSizeLimit(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong bytes, out error))
        {
            return true;
        }

        lowArgs.SetRegexSizeLimit(bytes);
        error = null;
        return true;
    }

    private static bool ParseRegexSizeLimit(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!TryParseHumanSize(value, flagName, out ulong size, out error))
        {
            return true;
        }

        lowArgs.SetRegexSizeLimit(size);
        error = null;
        return true;
    }

    private static bool ParseHostnameBin(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseHostnameBin(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in hostname command");
        return true;
    }

    private static bool ParseHostnameBin(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseHostnameBin(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in hostname command");
        return true;
    }

    private static bool ParseHostnameBin(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.SetHostnameBin(value);
        error = null;
        return true;
    }

    private static bool ParseHyperlinkFormat(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseHyperlinkFormat(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in hyperlink format");
        return true;
    }

    private static bool ParseHyperlinkFormat(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseHyperlinkFormat(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in hyperlink format");
        return true;
    }

    private static bool ParseHyperlinkFormat(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (!CliHyperlinkFormatParser.TryParse(value, out string normalized, out string? parseError))
        {
            error = new ScoutError($"error parsing flag --hyperlink-format: invalid hyperlink format: {parseError}");
            return true;
        }

        lowArgs.SetHyperlinkFormat(normalized);
        error = null;
        return true;
    }

    private static bool ParseSeparator(OsString value, string flagName, SeparatorKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParseSeparator(value.AsUnixBytes(), flagName, kind, lowArgs, out error);
        }

        return ParseSeparator(value.AsWindowsString(), kind, lowArgs, out error);
    }

    private static bool ParseSeparator(ReadOnlySpan<byte> value, string flagName, SeparatorKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = CliByteEscape.Unescape(value);
        SetSeparator(lowArgs, kind, separator);
        error = null;
        return true;
    }

    private static bool ParseSeparator(string value, SeparatorKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = CliByteEscape.Unescape(value);
        SetSeparator(lowArgs, kind, separator);
        error = null;
        return true;
    }

    private static bool ParsePathSeparator(OsString value, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.IsUnixBytes)
        {
            return ParsePathSeparator(value.AsUnixBytes(), lowArgs, out error);
        }

        return ParsePathSeparator(value.AsWindowsString(), lowArgs, out error);
    }

    private static bool ParsePathSeparator(ReadOnlySpan<byte> value, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = CliByteEscape.Unescape(value);
        return SetPathSeparator(separator, Utf8.GetString(value), lowArgs, out error);
    }

    private static bool ParsePathSeparator(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = CliByteEscape.Unescape(value);
        return SetPathSeparator(separator, value, lowArgs, out error);
    }

    private static bool SetPathSeparator(byte[] separator, string displayValue, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (separator.Length == 0)
        {
            lowArgs.SetPathSeparator(null);
            error = null;
            return true;
        }

        if (separator.Length == 1)
        {
            lowArgs.SetPathSeparator(separator[0]);
            error = null;
            return true;
        }

        error = new ScoutError(
            $"error parsing flag --path-separator: A path separator must be exactly one byte, but the given separator is {separator.Length} bytes: {displayValue}\n" +
            "In some shells on Windows '/' is automatically expanded. Use '//' instead.");
        return true;
    }

    private static void SetSeparator(CliLowArgs lowArgs, SeparatorKind kind, ReadOnlySpan<byte> separator)
    {
        switch (kind)
        {
            case SeparatorKind.FieldMatch:
                lowArgs.SetFieldMatchSeparator(separator);
                break;

            case SeparatorKind.FieldContext:
                lowArgs.SetFieldContextSeparator(separator);
                break;

            case SeparatorKind.Context:
                lowArgs.SetContextSeparator(separator);
                break;
        }
    }

    private static bool ParseGlob(OsString value, string flagName, bool caseInsensitive, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseGlob(text, caseInsensitive, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in glob");
        return true;
    }

    private static bool ParseGlob(ReadOnlySpan<byte> value, string flagName, bool caseInsensitive, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseGlob(text, caseInsensitive, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in glob");
        return true;
    }

    private static bool ParseGlob(string value, bool caseInsensitive, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddGlobPattern(value, caseInsensitive);
        error = null;
        return true;
    }

    private static bool ParseIgnoreFile(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseIgnoreFile(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in ignore-file path");
        return true;
    }

    private static bool ParseIgnoreFile(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseIgnoreFile(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in ignore-file path");
        return true;
    }

    private static bool ParseIgnoreFile(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddIgnoreFile(value);
        error = null;
        return true;
    }

    private static bool ParsePreprocessor(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParsePreprocessor(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in preprocessor path");
        return true;
    }

    private static bool ParsePreprocessor(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParsePreprocessor(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in preprocessor path");
        return true;
    }

    private static bool ParsePreprocessor(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.SetPreprocessor(value);
        error = null;
        return true;
    }

    private static bool ParsePreprocessorGlob(OsString value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParsePreprocessorGlob(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in preprocessor glob");
        return true;
    }

    private static bool ParsePreprocessorGlob(ReadOnlySpan<byte> value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParsePreprocessorGlob(text, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in preprocessor glob");
        return true;
    }

    private static bool ParsePreprocessorGlob(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddPreprocessorGlob(value);
        error = null;
        return true;
    }

    private static bool ParseSort(OsString value, string flagName, bool reverse, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseSort(text, flagName, reverse, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in sort value");
        return true;
    }

    private static bool ParseSort(ReadOnlySpan<byte> value, string flagName, bool reverse, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseSort(text, flagName, reverse, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in sort value");
        return true;
    }

    private static bool ParseSort(string value, string flagName, bool reverse, CliLowArgs lowArgs, out ScoutError? error)
    {
        switch (value)
        {
            case "none":
                lowArgs.SetSortMode(null);
                error = null;
                return true;

            case "path":
                lowArgs.SetSortMode(new CliSortMode(reverse, CliSortKind.Path));
                error = null;
                return true;

            case "modified":
                lowArgs.SetSortMode(new CliSortMode(reverse, CliSortKind.LastModified));
                error = null;
                return true;

            case "accessed":
                lowArgs.SetSortMode(new CliSortMode(reverse, CliSortKind.LastAccessed));
                error = null;
                return true;

            case "created":
                lowArgs.SetSortMode(new CliSortMode(reverse, CliSortKind.Created));
                error = null;
                return true;

            default:
                error = new ScoutError($"error parsing flag {flagName}: choice '{value}' is unrecognized");
                return true;
        }
    }

    private static bool ParseTypeChange(OsString value, string flagName, CliTypeChangeKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (value.TryGetText(out string text))
        {
            return ParseTypeChange(text, kind, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in type value");
        return true;
    }

    private static bool ParseTypeChange(ReadOnlySpan<byte> value, string flagName, CliTypeChangeKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        if (TryDecodeUtf8(value, out string text))
        {
            return ParseTypeChange(text, kind, lowArgs, out error);
        }

        error = new ScoutError($"error parsing flag {flagName}: invalid UTF-8 in type value");
        return true;
    }

    private static bool ParseTypeChange(string value, CliTypeChangeKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        lowArgs.AddTypeChange(new CliTypeChange(kind, value));
        error = null;
        return true;
    }

    private static bool TryParseHumanSize(ReadOnlySpan<byte> value, string flagName, out ulong size, out ScoutError? error)
    {
        if (!TryDecodeUtf8(value, out string text))
        {
            size = 0;
            error = new ScoutError($"error parsing flag {flagName}: value is not valid UTF-8");
            return false;
        }

        return TryParseHumanSize(text, flagName, out size, out error);
    }

    private static bool TryParseHumanSize(string value, string flagName, out ulong size, out ScoutError? error)
    {
        if (!CliHumanSizeParser.TryParse(value, out size, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: invalid size: {parseError}");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseUnsigned(ReadOnlySpan<byte> value, out ulong count, out string parseError)
    {
        if (value.IsEmpty)
        {
            count = 0;
            parseError = "cannot parse integer from empty string";
            return false;
        }

        ulong parsed = 0;
        for (int index = 0; index < value.Length; index++)
        {
            byte digit = value[index];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                count = 0;
                parseError = "invalid digit found in string";
                return false;
            }

            ulong numericDigit = (ulong)(digit - (byte)'0');
            if (parsed > (ulong.MaxValue - numericDigit) / 10)
            {
                count = 0;
                parseError = "number too large to fit in target type";
                return false;
            }

            parsed = (parsed * 10) + numericDigit;
        }

        count = parsed;
        parseError = string.Empty;
        return true;
    }

    private static bool TryParseUnsigned(string value, out ulong count, out string parseError)
    {
        if (value.Length == 0)
        {
            count = 0;
            parseError = "cannot parse integer from empty string";
            return false;
        }

        ulong parsed = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char digit = value[index];
            if (digit is < '0' or > '9')
            {
                count = 0;
                parseError = "invalid digit found in string";
                return false;
            }

            ulong numericDigit = (ulong)(digit - '0');
            if (parsed > (ulong.MaxValue - numericDigit) / 10)
            {
                count = 0;
                parseError = "number too large to fit in target type";
                return false;
            }

            parsed = (parsed * 10) + numericDigit;
        }

        count = parsed;
        parseError = string.Empty;
        return true;
    }

    private static bool TryParseUnknownFlag(OsString argument, out ScoutError error)
    {
        if (argument.IsUnixBytes)
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (bytes.Length > 2 && bytes.StartsWith("--"u8))
            {
                error = TryDecodeUtf8(bytes[2..], out string name)
                    ? new ScoutError($"unrecognized flag --{name}")
                    : new ScoutError("invalid CLI arguments");
                return true;
            }

            if (bytes.Length > 1 && bytes[0] == (byte)'-')
            {
                error = TryDecodeUtf8(bytes[1..2], out string name)
                    ? new ScoutError($"unrecognized flag -{name}")
                    : new ScoutError("invalid CLI arguments");
                return true;
            }
        }
        else if (argument.TryGetText(out string text))
        {
            if (text.Length > 2 && text.StartsWith("--", StringComparison.Ordinal))
            {
                error = new ScoutError($"unrecognized flag --{text[2..]}");
                return true;
            }

            if (text.Length > 1 && text[0] == '-')
            {
                error = new ScoutError($"unrecognized flag -{text[1]}");
                return true;
            }
        }

        error = new ScoutError("unreachable parser state");
        return false;
    }

    private static bool TextEquals(OsString argument, string expected)
    {
        return argument.TryGetText(out string text) && string.Equals(text, expected, StringComparison.Ordinal);
    }

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = Utf8Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static readonly UTF8Encoding Utf8 =
        new(encoderShouldEmitUTF8Identifier: false);

    private static readonly UTF8Encoding Utf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
