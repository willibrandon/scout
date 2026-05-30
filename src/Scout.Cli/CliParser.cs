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

            if (TryParseSearchFlag(argument, lowArgs, out ScoutError? searchError))
            {
                if (searchError is not null)
                {
                    return CliParseResult.Fail(searchError);
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

    private static bool TryParseRepeatedUnrestricted(OsString argument, CliLowArgs lowArgs, out ScoutError? error)
    {
        error = null;
        if (argument.IsUnixBytes)
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (bytes.Length <= 2 || bytes[0] != (byte)'-')
            {
                return false;
            }

            for (int index = 1; index < bytes.Length; index++)
            {
                if (bytes[index] != (byte)'u')
                {
                    return false;
                }
            }

            return ParseUnrestricted(bytes.Length - 1, "-u", lowArgs, out error);
        }

        if (!argument.TryGetText(out string text) || text.Length <= 2 || text[0] != '-')
        {
            return false;
        }

        for (int index = 1; index < text.Length; index++)
        {
            if (text[index] != 'u')
            {
                return false;
            }
        }

        return ParseUnrestricted(text.Length - 1, "-u", lowArgs, out error);
    }

    private static bool ParseUnrestricted(int count, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        for (int index = 0; index < count; index++)
        {
            if (lowArgs.UnrestrictedCount >= 3)
            {
                error = new ScoutError($"error parsing flag {flagName}: flag can only be repeated up to 3 times");
                return true;
            }

            lowArgs.AddUnrestrictedLevel();
        }

        error = null;
        return true;
    }

    private static bool TryParseSearchFlag(OsString argument, CliLowArgs lowArgs, out ScoutError? error)
    {
        error = null;
        if (argument.EqualsUnixBytes("--files"u8) || TextEquals(argument, "--files"))
        {
            lowArgs.SetSearchMode(CliSearchMode.Files);
            return true;
        }

        if (argument.EqualsUnixBytes("-c"u8) || TextEquals(argument, "-c") || argument.EqualsUnixBytes("--count"u8) || TextEquals(argument, "--count"))
        {
            lowArgs.SetSearchMode(CliSearchMode.Count);
            return true;
        }

        if (argument.EqualsUnixBytes("--count-matches"u8) || TextEquals(argument, "--count-matches"))
        {
            lowArgs.SetSearchMode(CliSearchMode.CountMatches);
            return true;
        }

        if (argument.EqualsUnixBytes("-l"u8) || TextEquals(argument, "-l") || argument.EqualsUnixBytes("--files-with-matches"u8) || TextEquals(argument, "--files-with-matches"))
        {
            lowArgs.SetSearchMode(CliSearchMode.FilesWithMatches);
            return true;
        }

        if (argument.EqualsUnixBytes("--files-without-match"u8) || TextEquals(argument, "--files-without-match"))
        {
            lowArgs.SetSearchMode(CliSearchMode.FilesWithoutMatch);
            return true;
        }

        if (argument.EqualsUnixBytes("--json"u8) || TextEquals(argument, "--json"))
        {
            lowArgs.SetSearchMode(CliSearchMode.Json);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-json"u8) || TextEquals(argument, "--no-json"))
        {
            lowArgs.ClearJsonMode();
            return true;
        }

        if (argument.EqualsUnixBytes("-F"u8) || TextEquals(argument, "-F") || argument.EqualsUnixBytes("--fixed-strings"u8) || TextEquals(argument, "--fixed-strings"))
        {
            lowArgs.SetFixedStrings(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-fixed-strings"u8) || TextEquals(argument, "--no-fixed-strings"))
        {
            lowArgs.SetFixedStrings(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-P"u8) || TextEquals(argument, "-P") || argument.EqualsUnixBytes("--pcre2"u8) || TextEquals(argument, "--pcre2"))
        {
            lowArgs.SetRegexEngine(CliRegexEngine.Pcre2);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-pcre2"u8) || TextEquals(argument, "--no-pcre2"))
        {
            lowArgs.SetRegexEngine(CliRegexEngine.Default);
            return true;
        }

        if (argument.EqualsUnixBytes("--pcre2-unicode"u8) || TextEquals(argument, "--pcre2-unicode"))
        {
            lowArgs.SetPcre2Unicode(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-pcre2-unicode"u8) || TextEquals(argument, "--no-pcre2-unicode"))
        {
            lowArgs.SetPcre2Unicode(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--auto-hybrid-regex"u8) || TextEquals(argument, "--auto-hybrid-regex"))
        {
            lowArgs.SetAutoHybridRegex(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-auto-hybrid-regex"u8) || TextEquals(argument, "--no-auto-hybrid-regex"))
        {
            lowArgs.SetAutoHybridRegex(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-a"u8) || TextEquals(argument, "-a") || argument.EqualsUnixBytes("--text"u8) || TextEquals(argument, "--text"))
        {
            lowArgs.SetTextMode(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-text"u8) || TextEquals(argument, "--no-text"))
        {
            lowArgs.SetTextMode(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--binary"u8) || TextEquals(argument, "--binary"))
        {
            lowArgs.SetSearchBinaryFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-binary"u8) || TextEquals(argument, "--no-binary"))
        {
            lowArgs.SetSearchBinaryFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-u"u8) || TextEquals(argument, "-u") || argument.EqualsUnixBytes("--unrestricted"u8) || TextEquals(argument, "--unrestricted"))
        {
            return ParseUnrestricted(count: 1, argument.EqualsUnixBytes("--unrestricted"u8) || TextEquals(argument, "--unrestricted") ? "--unrestricted" : "-u", lowArgs, out error);
        }

        if (TryParseRepeatedUnrestricted(argument, lowArgs, out error))
        {
            return true;
        }

        if (argument.EqualsUnixBytes("-q"u8) || TextEquals(argument, "-q") || argument.EqualsUnixBytes("--quiet"u8) || TextEquals(argument, "--quiet"))
        {
            lowArgs.SetQuiet(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--stats"u8) || TextEquals(argument, "--stats"))
        {
            lowArgs.SetStats(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-stats"u8) || TextEquals(argument, "--no-stats"))
        {
            lowArgs.SetStats(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--stop-on-nonmatch"u8) || TextEquals(argument, "--stop-on-nonmatch"))
        {
            lowArgs.SetStopOnNonmatch(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-U"u8) || TextEquals(argument, "-U") || argument.EqualsUnixBytes("--multiline"u8) || TextEquals(argument, "--multiline"))
        {
            lowArgs.SetMultiline(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-multiline"u8) || TextEquals(argument, "--no-multiline"))
        {
            lowArgs.SetMultiline(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--multiline-dotall"u8) || TextEquals(argument, "--multiline-dotall"))
        {
            lowArgs.SetMultilineDotall(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-multiline-dotall"u8) || TextEquals(argument, "--no-multiline-dotall"))
        {
            lowArgs.SetMultilineDotall(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-unicode"u8) || TextEquals(argument, "--no-unicode"))
        {
            lowArgs.SetUnicode(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--unicode"u8) || TextEquals(argument, "--unicode"))
        {
            lowArgs.SetUnicode(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--debug"u8) || TextEquals(argument, "--debug"))
        {
            lowArgs.SetLoggingMode(CliLoggingMode.Debug);
            return true;
        }

        if (argument.EqualsUnixBytes("--trace"u8) || TextEquals(argument, "--trace"))
        {
            lowArgs.SetLoggingMode(CliLoggingMode.Trace);
            return true;
        }

        if (argument.EqualsUnixBytes("--crlf"u8) || TextEquals(argument, "--crlf"))
        {
            lowArgs.SetCrlf(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-crlf"u8) || TextEquals(argument, "--no-crlf"))
        {
            lowArgs.SetCrlf(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--null-data"u8) || TextEquals(argument, "--null-data"))
        {
            lowArgs.SetNullData(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-encoding"u8) || TextEquals(argument, "--no-encoding"))
        {
            lowArgs.SetEncodingMode(CliEncodingMode.Auto);
            return true;
        }

        if (argument.EqualsUnixBytes("--messages"u8) || TextEquals(argument, "--messages"))
        {
            lowArgs.SetMessages(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-messages"u8) || TextEquals(argument, "--no-messages"))
        {
            lowArgs.SetMessages(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--line-buffered"u8) || TextEquals(argument, "--line-buffered"))
        {
            lowArgs.SetBufferMode(CliBufferMode.Line);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-line-buffered"u8) || TextEquals(argument, "--no-line-buffered"))
        {
            lowArgs.SetBufferMode(CliBufferMode.Auto);
            return true;
        }

        if (argument.EqualsUnixBytes("--block-buffered"u8) || TextEquals(argument, "--block-buffered"))
        {
            lowArgs.SetBufferMode(CliBufferMode.Block);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-block-buffered"u8) || TextEquals(argument, "--no-block-buffered"))
        {
            lowArgs.SetBufferMode(CliBufferMode.Auto);
            return true;
        }

        if (argument.EqualsUnixBytes("--mmap"u8) || TextEquals(argument, "--mmap"))
        {
            lowArgs.SetMmapMode(CliMmapMode.AlwaysTryMmap);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-mmap"u8) || TextEquals(argument, "--no-mmap"))
        {
            lowArgs.SetMmapMode(CliMmapMode.Never);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-config"u8) || TextEquals(argument, "--no-config"))
        {
            return true;
        }

        if (argument.EqualsUnixBytes("-z"u8) || TextEquals(argument, "-z") || argument.EqualsUnixBytes("--search-zip"u8) || TextEquals(argument, "--search-zip"))
        {
            lowArgs.SetSearchZip(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-search-zip"u8) || TextEquals(argument, "--no-search-zip"))
        {
            lowArgs.SetSearchZip(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-pre"u8) || TextEquals(argument, "--no-pre"))
        {
            lowArgs.SetPreprocessor(null);
            return true;
        }

        if (argument.EqualsUnixBytes("-o"u8) || TextEquals(argument, "-o") || argument.EqualsUnixBytes("--only-matching"u8) || TextEquals(argument, "--only-matching"))
        {
            lowArgs.SetOnlyMatching(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--vimgrep"u8) || TextEquals(argument, "--vimgrep"))
        {
            lowArgs.SetVimgrep(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-p"u8) || TextEquals(argument, "-p") || argument.EqualsUnixBytes("--pretty"u8) || TextEquals(argument, "--pretty"))
        {
            lowArgs.SetPretty();
            return true;
        }

        if (argument.EqualsUnixBytes("--max-columns-preview"u8) || TextEquals(argument, "--max-columns-preview"))
        {
            lowArgs.SetMaxColumnsPreview(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-max-columns-preview"u8) || TextEquals(argument, "--no-max-columns-preview"))
        {
            lowArgs.SetMaxColumnsPreview(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--include-zero"u8) || TextEquals(argument, "--include-zero"))
        {
            lowArgs.SetIncludeZero(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-include-zero"u8) || TextEquals(argument, "--no-include-zero"))
        {
            lowArgs.SetIncludeZero(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-0"u8) || TextEquals(argument, "-0") || argument.EqualsUnixBytes("--null"u8) || TextEquals(argument, "--null"))
        {
            lowArgs.SetNullPathTerminator(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-v"u8) || TextEquals(argument, "-v") || argument.EqualsUnixBytes("--invert-match"u8) || TextEquals(argument, "--invert-match"))
        {
            lowArgs.SetInvertMatch(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-invert-match"u8) || TextEquals(argument, "--no-invert-match"))
        {
            lowArgs.SetInvertMatch(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-x"u8) || TextEquals(argument, "-x") || argument.EqualsUnixBytes("--line-regexp"u8) || TextEquals(argument, "--line-regexp"))
        {
            lowArgs.SetLineRegexp(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-w"u8) || TextEquals(argument, "-w") || argument.EqualsUnixBytes("--word-regexp"u8) || TextEquals(argument, "--word-regexp"))
        {
            lowArgs.SetWordRegexp(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-H"u8) || TextEquals(argument, "-H") || argument.EqualsUnixBytes("--with-filename"u8) || TextEquals(argument, "--with-filename"))
        {
            lowArgs.SetWithFilename(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-I"u8) || TextEquals(argument, "-I") || argument.EqualsUnixBytes("--no-filename"u8) || TextEquals(argument, "--no-filename"))
        {
            lowArgs.SetWithFilename(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-i"u8) || TextEquals(argument, "-i") || argument.EqualsUnixBytes("--ignore-case"u8) || TextEquals(argument, "--ignore-case"))
        {
            lowArgs.SetCaseMode(CliCaseMode.Insensitive);
            return true;
        }

        if (argument.EqualsUnixBytes("-s"u8) || TextEquals(argument, "-s") || argument.EqualsUnixBytes("--case-sensitive"u8) || TextEquals(argument, "--case-sensitive"))
        {
            lowArgs.SetCaseMode(CliCaseMode.Sensitive);
            return true;
        }

        if (argument.EqualsUnixBytes("-S"u8) || TextEquals(argument, "-S") || argument.EqualsUnixBytes("--smart-case"u8) || TextEquals(argument, "--smart-case"))
        {
            lowArgs.SetCaseMode(CliCaseMode.Smart);
            return true;
        }

        if (argument.EqualsUnixBytes("-n"u8) || TextEquals(argument, "-n") || argument.EqualsUnixBytes("--line-number"u8) || TextEquals(argument, "--line-number"))
        {
            lowArgs.SetLineNumber(true);
            return true;
        }

        if (argument.EqualsUnixBytes("-N"u8) || TextEquals(argument, "-N") || argument.EqualsUnixBytes("--no-line-number"u8) || TextEquals(argument, "--no-line-number"))
        {
            lowArgs.SetLineNumber(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--column"u8) || TextEquals(argument, "--column"))
        {
            lowArgs.SetColumn(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-column"u8) || TextEquals(argument, "--no-column"))
        {
            lowArgs.SetColumn(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-b"u8) || TextEquals(argument, "-b") || argument.EqualsUnixBytes("--byte-offset"u8) || TextEquals(argument, "--byte-offset"))
        {
            lowArgs.SetByteOffset(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-byte-offset"u8) || TextEquals(argument, "--no-byte-offset"))
        {
            lowArgs.SetByteOffset(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-."u8) || TextEquals(argument, "-.") || argument.EqualsUnixBytes("--hidden"u8) || TextEquals(argument, "--hidden"))
        {
            lowArgs.SetIncludeHidden(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-hidden"u8) || TextEquals(argument, "--no-hidden"))
        {
            lowArgs.SetIncludeHidden(false);
            return true;
        }

        if (argument.EqualsUnixBytes("-L"u8) || TextEquals(argument, "-L") || argument.EqualsUnixBytes("--follow"u8) || TextEquals(argument, "--follow"))
        {
            lowArgs.SetFollowLinks(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-follow"u8) || TextEquals(argument, "--no-follow"))
        {
            lowArgs.SetFollowLinks(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore"u8) || TextEquals(argument, "--no-ignore"))
        {
            lowArgs.SetRespectIgnoreFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore"u8) || TextEquals(argument, "--ignore"))
        {
            lowArgs.SetRespectIgnoreFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-dot"u8) || TextEquals(argument, "--no-ignore-dot"))
        {
            lowArgs.SetRespectDotIgnoreFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-dot"u8) || TextEquals(argument, "--ignore-dot"))
        {
            lowArgs.SetRespectDotIgnoreFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-vcs"u8) || TextEquals(argument, "--no-ignore-vcs"))
        {
            lowArgs.SetRespectGitIgnoreFiles(false);
            lowArgs.SetRespectGitExcludeFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-vcs"u8) || TextEquals(argument, "--ignore-vcs"))
        {
            lowArgs.SetRespectGitIgnoreFiles(true);
            lowArgs.SetRespectGitExcludeFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-exclude"u8) || TextEquals(argument, "--no-ignore-exclude"))
        {
            lowArgs.SetRespectGitExcludeFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-exclude"u8) || TextEquals(argument, "--ignore-exclude"))
        {
            lowArgs.SetRespectGitExcludeFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-global"u8) || TextEquals(argument, "--no-ignore-global"))
        {
            lowArgs.SetRespectGlobalIgnoreFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-global"u8) || TextEquals(argument, "--ignore-global"))
        {
            lowArgs.SetRespectGlobalIgnoreFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-messages"u8) || TextEquals(argument, "--no-ignore-messages"))
        {
            lowArgs.SetIgnoreMessages(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-messages"u8) || TextEquals(argument, "--ignore-messages"))
        {
            lowArgs.SetIgnoreMessages(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-parent"u8) || TextEquals(argument, "--no-ignore-parent"))
        {
            lowArgs.SetRespectParentIgnoreFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-parent"u8) || TextEquals(argument, "--ignore-parent"))
        {
            lowArgs.SetRespectParentIgnoreFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-files"u8) || TextEquals(argument, "--no-ignore-files"))
        {
            lowArgs.SetRespectExplicitIgnoreFiles(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-files"u8) || TextEquals(argument, "--ignore-files"))
        {
            lowArgs.SetRespectExplicitIgnoreFiles(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-require-git"u8) || TextEquals(argument, "--no-require-git"))
        {
            lowArgs.SetRequireGitRepository(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--require-git"u8) || TextEquals(argument, "--require-git"))
        {
            lowArgs.SetRequireGitRepository(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--ignore-file-case-insensitive"u8) || TextEquals(argument, "--ignore-file-case-insensitive"))
        {
            lowArgs.SetIgnoreFileCaseInsensitive(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-ignore-file-case-insensitive"u8) || TextEquals(argument, "--no-ignore-file-case-insensitive"))
        {
            lowArgs.SetIgnoreFileCaseInsensitive(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--one-file-system"u8) || TextEquals(argument, "--one-file-system"))
        {
            lowArgs.SetOneFileSystem(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-one-file-system"u8) || TextEquals(argument, "--no-one-file-system"))
        {
            lowArgs.SetOneFileSystem(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--glob-case-insensitive"u8) || TextEquals(argument, "--glob-case-insensitive"))
        {
            lowArgs.SetGlobCaseInsensitive(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-glob-case-insensitive"u8) || TextEquals(argument, "--no-glob-case-insensitive"))
        {
            lowArgs.SetGlobCaseInsensitive(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--type-list"u8) || TextEquals(argument, "--type-list"))
        {
            lowArgs.SetTypeList(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--trim"u8) || TextEquals(argument, "--trim"))
        {
            lowArgs.SetTrim(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-trim"u8) || TextEquals(argument, "--no-trim"))
        {
            lowArgs.SetTrim(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--heading"u8) || TextEquals(argument, "--heading"))
        {
            lowArgs.SetHeading(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-heading"u8) || TextEquals(argument, "--no-heading"))
        {
            lowArgs.SetHeading(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--passthru"u8) || TextEquals(argument, "--passthru") || argument.EqualsUnixBytes("--passthrough"u8) || TextEquals(argument, "--passthrough"))
        {
            lowArgs.SetPassthru(true);
            return true;
        }

        if (argument.EqualsUnixBytes("--no-context-separator"u8) || TextEquals(argument, "--no-context-separator"))
        {
            lowArgs.SetContextSeparatorEnabled(false);
            return true;
        }

        if (argument.EqualsUnixBytes("--sort-files"u8) || TextEquals(argument, "--sort-files"))
        {
            lowArgs.SetSortMode(new CliSortMode(reverse: false, CliSortKind.Path));
            return true;
        }

        if (argument.EqualsUnixBytes("--no-sort-files"u8) || TextEquals(argument, "--no-sort-files"))
        {
            lowArgs.SetSortMode(null);
            return true;
        }

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

        switch (flag)
        {
            case 'c':
                lowArgs.SetSearchMode(CliSearchMode.Count);
                return true;

            case 'l':
                lowArgs.SetSearchMode(CliSearchMode.FilesWithMatches);
                return true;

            case 'F':
                lowArgs.SetFixedStrings(true);
                return true;

            case 'P':
                lowArgs.SetRegexEngine(CliRegexEngine.Pcre2);
                return true;

            case 'a':
                lowArgs.SetTextMode(true);
                return true;

            case 'u':
                return ParseUnrestricted(count: 1, "-u", lowArgs, out error);

            case 'q':
                lowArgs.SetQuiet(true);
                return true;

            case 'U':
                lowArgs.SetMultiline(true);
                return true;

            case 'z':
                lowArgs.SetSearchZip(true);
                return true;

            case 'o':
                lowArgs.SetOnlyMatching(true);
                return true;

            case 'p':
                lowArgs.SetPretty();
                return true;

            case '0':
                lowArgs.SetNullPathTerminator(true);
                return true;

            case 'v':
                lowArgs.SetInvertMatch(true);
                return true;

            case 'x':
                lowArgs.SetLineRegexp(true);
                return true;

            case 'w':
                lowArgs.SetWordRegexp(true);
                return true;

            case 'H':
                lowArgs.SetWithFilename(true);
                return true;

            case 'I':
                lowArgs.SetWithFilename(false);
                return true;

            case 'i':
                lowArgs.SetCaseMode(CliCaseMode.Insensitive);
                return true;

            case 's':
                lowArgs.SetCaseMode(CliCaseMode.Sensitive);
                return true;

            case 'S':
                lowArgs.SetCaseMode(CliCaseMode.Smart);
                return true;

            case 'n':
                lowArgs.SetLineNumber(true);
                return true;

            case 'N':
                lowArgs.SetLineNumber(false);
                return true;

            case 'b':
                lowArgs.SetByteOffset(true);
                return true;

            case '.':
                lowArgs.SetIncludeHidden(true);
                return true;

            case 'L':
                lowArgs.SetFollowLinks(true);
                return true;

            default:
                return false;
        }
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

        if (argument.EqualsUnixBytes("-m"u8) || TextEquals(argument, "-m"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-m", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxCount(value, "-m", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--max-count"u8) || TextEquals(argument, "--max-count"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--max-count", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxCount(value, "--max-count", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-M"u8) || TextEquals(argument, "-M"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-M", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxColumns(value, "-M", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--max-columns"u8) || TextEquals(argument, "--max-columns"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--max-columns", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxColumns(value, "--max-columns", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-j"u8) || TextEquals(argument, "-j"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-j", out OsString value, out error))
            {
                return true;
            }

            return ParseThreads(value, "-j", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--threads"u8) || TextEquals(argument, "--threads"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--threads", out OsString value, out error))
            {
                return true;
            }

            return ParseThreads(value, "--threads", lowArgs, out error);
        }

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

        if (argument.EqualsUnixBytes("-A"u8) || TextEquals(argument, "-A"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-A", out OsString value, out error))
            {
                return true;
            }

            return ParseAfterContext(value, "-A", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--after-context"u8) || TextEquals(argument, "--after-context"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--after-context", out OsString value, out error))
            {
                return true;
            }

            return ParseAfterContext(value, "--after-context", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-B"u8) || TextEquals(argument, "-B"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-B", out OsString value, out error))
            {
                return true;
            }

            return ParseBeforeContext(value, "-B", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--before-context"u8) || TextEquals(argument, "--before-context"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--before-context", out OsString value, out error))
            {
                return true;
            }

            return ParseBeforeContext(value, "--before-context", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-C"u8) || TextEquals(argument, "-C"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-C", out OsString value, out error))
            {
                return true;
            }

            return ParseContext(value, "-C", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--context"u8) || TextEquals(argument, "--context"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--context", out OsString value, out error))
            {
                return true;
            }

            return ParseContext(value, "--context", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("-d"u8) || TextEquals(argument, "-d"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "-d", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxDepth(value, "-d", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--max-depth"u8) || TextEquals(argument, "--max-depth"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--max-depth", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxDepth(value, "--max-depth", lowArgs, out error);
        }

        if (argument.EqualsUnixBytes("--maxdepth"u8) || TextEquals(argument, "--maxdepth"))
        {
            if (!TryGetFollowingValue(arguments, ref index, "--maxdepth", out OsString value, out error))
            {
                return true;
            }

            return ParseMaxDepth(value, "--maxdepth", lowArgs, out error);
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

        if (TryGetInlineUnixValue(argument, "--max-count="u8, out ReadOnlySpan<byte> longValue))
        {
            return ParseMaxCount(longValue, "--max-count", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--max-columns="u8, out ReadOnlySpan<byte> maxColumnsValue))
        {
            return ParseMaxColumns(maxColumnsValue, "--max-columns", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--threads="u8, out ReadOnlySpan<byte> threadsValue))
        {
            return ParseThreads(threadsValue, "--threads", lowArgs, out error);
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

        if (TryGetInlineUnixValue(argument, "-m"u8, out ReadOnlySpan<byte> shortValue))
        {
            if (!shortValue.IsEmpty && shortValue[0] == (byte)'=')
            {
                shortValue = shortValue[1..];
            }

            return ParseMaxCount(shortValue, "-m", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-M"u8, out ReadOnlySpan<byte> shortMaxColumnsValue))
        {
            if (!shortMaxColumnsValue.IsEmpty && shortMaxColumnsValue[0] == (byte)'=')
            {
                shortMaxColumnsValue = shortMaxColumnsValue[1..];
            }

            return ParseMaxColumns(shortMaxColumnsValue, "-M", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-j"u8, out ReadOnlySpan<byte> shortThreadsValue))
        {
            if (!shortThreadsValue.IsEmpty && shortThreadsValue[0] == (byte)'=')
            {
                shortThreadsValue = shortThreadsValue[1..];
            }

            return ParseThreads(shortThreadsValue, "-j", lowArgs, out error);
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

        if (TryGetInlineUnixValue(argument, "--after-context="u8, out ReadOnlySpan<byte> afterContextValue))
        {
            return ParseAfterContext(afterContextValue, "--after-context", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-A"u8, out ReadOnlySpan<byte> shortAfterContextValue))
        {
            if (!shortAfterContextValue.IsEmpty && shortAfterContextValue[0] == (byte)'=')
            {
                shortAfterContextValue = shortAfterContextValue[1..];
            }

            return ParseAfterContext(shortAfterContextValue, "-A", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--before-context="u8, out ReadOnlySpan<byte> beforeContextValue))
        {
            return ParseBeforeContext(beforeContextValue, "--before-context", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-B"u8, out ReadOnlySpan<byte> shortBeforeContextValue))
        {
            if (!shortBeforeContextValue.IsEmpty && shortBeforeContextValue[0] == (byte)'=')
            {
                shortBeforeContextValue = shortBeforeContextValue[1..];
            }

            return ParseBeforeContext(shortBeforeContextValue, "-B", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--context="u8, out ReadOnlySpan<byte> contextValue))
        {
            return ParseContext(contextValue, "--context", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-C"u8, out ReadOnlySpan<byte> shortContextValue))
        {
            if (!shortContextValue.IsEmpty && shortContextValue[0] == (byte)'=')
            {
                shortContextValue = shortContextValue[1..];
            }

            return ParseContext(shortContextValue, "-C", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--max-depth="u8, out ReadOnlySpan<byte> maxDepthValue))
        {
            return ParseMaxDepth(maxDepthValue, "--max-depth", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--maxdepth="u8, out ReadOnlySpan<byte> maxDepthAliasValue))
        {
            return ParseMaxDepth(maxDepthAliasValue, "--maxdepth", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "--max-filesize="u8, out ReadOnlySpan<byte> maxFileSizeValue))
        {
            return ParseMaxFileSize(maxFileSizeValue, "--max-filesize", lowArgs, out error);
        }

        if (TryGetInlineUnixValue(argument, "-d"u8, out ReadOnlySpan<byte> shortDepthValue))
        {
            if (!shortDepthValue.IsEmpty && shortDepthValue[0] == (byte)'=')
            {
                shortDepthValue = shortDepthValue[1..];
            }

            return ParseMaxDepth(shortDepthValue, "-d", lowArgs, out error);
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
            if (text.StartsWith("--max-count=", StringComparison.Ordinal))
            {
                return ParseMaxCount(text["--max-count=".Length..], "--max-count", lowArgs, out error);
            }

            if (text.StartsWith("--max-columns=", StringComparison.Ordinal))
            {
                return ParseMaxColumns(text["--max-columns=".Length..], "--max-columns", lowArgs, out error);
            }

            if (text.StartsWith("--threads=", StringComparison.Ordinal))
            {
                return ParseThreads(text["--threads=".Length..], "--threads", lowArgs, out error);
            }

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

            if (text.Length > 2 && text.StartsWith("-m", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseMaxCount(value, "-m", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-M", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseMaxColumns(value, "-M", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-j", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseThreads(value, "-j", lowArgs, out error);
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

            if (text.StartsWith("--after-context=", StringComparison.Ordinal))
            {
                return ParseAfterContext(text["--after-context=".Length..], "--after-context", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-A", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseAfterContext(value, "-A", lowArgs, out error);
            }

            if (text.StartsWith("--before-context=", StringComparison.Ordinal))
            {
                return ParseBeforeContext(text["--before-context=".Length..], "--before-context", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-B", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseBeforeContext(value, "-B", lowArgs, out error);
            }

            if (text.StartsWith("--context=", StringComparison.Ordinal))
            {
                return ParseContext(text["--context=".Length..], "--context", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-C", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseContext(value, "-C", lowArgs, out error);
            }

            if (text.StartsWith("--max-depth=", StringComparison.Ordinal))
            {
                return ParseMaxDepth(text["--max-depth=".Length..], "--max-depth", lowArgs, out error);
            }

            if (text.StartsWith("--maxdepth=", StringComparison.Ordinal))
            {
                return ParseMaxDepth(text["--maxdepth=".Length..], "--maxdepth", lowArgs, out error);
            }

            if (text.StartsWith("--max-filesize=", StringComparison.Ordinal))
            {
                return ParseMaxFileSize(text["--max-filesize=".Length..], "--max-filesize", lowArgs, out error);
            }

            if (text.Length > 2 && text.StartsWith("-d", StringComparison.Ordinal))
            {
                string value = text[2] == '=' ? text[3..] : text[2..];
                return ParseMaxDepth(value, "-d", lowArgs, out error);
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
        string original = Utf8.GetString(value);
        if (!TryParseSize(value, original, out ulong bytes, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
            return true;
        }

        lowArgs.SetMaxFileSize(bytes);
        error = null;
        return true;
    }

    private static bool ParseMaxFileSize(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] bytes = Utf8.GetBytes(value);
        if (!TryParseSize(bytes, value, out ulong size, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
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
        string original = Utf8.GetString(value);
        if (!TryParseSize(value, original, out ulong bytes, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
            return true;
        }

        lowArgs.SetDfaSizeLimit(bytes);
        error = null;
        return true;
    }

    private static bool ParseDfaSizeLimit(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] bytes = Utf8.GetBytes(value);
        if (!TryParseSize(bytes, value, out ulong size, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
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
        string original = Utf8.GetString(value);
        if (!TryParseSize(value, original, out ulong bytes, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
            return true;
        }

        lowArgs.SetRegexSizeLimit(bytes);
        error = null;
        return true;
    }

    private static bool ParseRegexSizeLimit(string value, string flagName, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] bytes = Utf8.GetBytes(value);
        if (!TryParseSize(bytes, value, out ulong size, out string parseError))
        {
            error = new ScoutError($"error parsing flag {flagName}: {parseError}");
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
        byte[] separator = UnescapeSeparator(value);
        SetSeparator(lowArgs, kind, separator);
        error = null;
        return true;
    }

    private static bool ParseSeparator(string value, SeparatorKind kind, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = UnescapeSeparator(Utf8.GetBytes(value));
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
        byte[] separator = UnescapeSeparator(value);
        return SetPathSeparator(separator, Utf8.GetString(value), lowArgs, out error);
    }

    private static bool ParsePathSeparator(string value, CliLowArgs lowArgs, out ScoutError? error)
    {
        byte[] separator = UnescapeSeparator(Utf8.GetBytes(value));
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

    private static byte[] UnescapeSeparator(ReadOnlySpan<byte> value)
    {
        var bytes = new List<byte>(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            byte current = value[index];
            if (current != (byte)'\\' || index + 1 >= value.Length)
            {
                bytes.Add(current);
                continue;
            }

            byte escaped = value[index + 1];
            if (escaped == (byte)'x' && index + 3 < value.Length && TryGetHex(value[index + 2], out byte high) && TryGetHex(value[index + 3], out byte low))
            {
                bytes.Add((byte)((high << 4) | low));
                index += 3;
                continue;
            }

            if (TryGetEscapedByte(escaped, out byte unescaped))
            {
                bytes.Add(unescaped);
                index++;
                continue;
            }

            bytes.Add(current);
            bytes.Add(escaped);
            index++;
        }

        return bytes.ToArray();
    }

    private static bool TryGetEscapedByte(byte escaped, out byte value)
    {
        switch (escaped)
        {
            case (byte)'0':
                value = 0;
                return true;

            case (byte)'a':
                value = 0x07;
                return true;

            case (byte)'b':
                value = 0x08;
                return true;

            case (byte)'f':
                value = 0x0c;
                return true;

            case (byte)'n':
                value = (byte)'\n';
                return true;

            case (byte)'r':
                value = (byte)'\r';
                return true;

            case (byte)'t':
                value = (byte)'\t';
                return true;

            case (byte)'v':
                value = 0x0b;
                return true;

            default:
                value = 0;
                return false;
        }
    }

    private static bool TryGetHex(byte value, out byte hex)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            hex = (byte)(value - (byte)'0');
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            hex = (byte)(value - (byte)'a' + 10);
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            hex = (byte)(value - (byte)'A' + 10);
            return true;
        }

        hex = 0;
        return false;
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

    private static bool TryParseSize(ReadOnlySpan<byte> value, string original, out ulong size, out string parseError)
    {
        if (value.IsEmpty)
        {
            return InvalidSizeFormat(original, out size, out parseError);
        }

        ulong multiplier = 1;
        int digitLength = value.Length;
        byte last = value[^1];
        if (last == (byte)'K')
        {
            multiplier = 1024;
            digitLength--;
        }
        else if (last == (byte)'M')
        {
            multiplier = 1024 * 1024;
            digitLength--;
        }
        else if (last == (byte)'G')
        {
            multiplier = 1024UL * 1024UL * 1024UL;
            digitLength--;
        }
        else if (last is < (byte)'0' or > (byte)'9')
        {
            return InvalidSizeFormat(original, out size, out parseError);
        }

        if (digitLength == 0)
        {
            return InvalidSizeFormat(original, out size, out parseError);
        }

        ulong parsed = 0;
        for (int index = 0; index < digitLength; index++)
        {
            byte digit = value[index];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return InvalidSizeFormat(original, out size, out parseError);
            }

            ulong numericDigit = (ulong)(digit - (byte)'0');
            if (parsed > (ulong.MaxValue - numericDigit) / 10)
            {
                size = 0;
                parseError = $"invalid size: invalid integer found in size '{original}': number too large to fit in target type";
                return false;
            }

            parsed = (parsed * 10) + numericDigit;
        }

        if (parsed > ulong.MaxValue / multiplier)
        {
            size = 0;
            parseError = $"invalid size: size too big in '{original}'";
            return false;
        }

        size = parsed * multiplier;
        parseError = string.Empty;
        return true;
    }

    private static bool InvalidSizeFormat(string original, out ulong size, out string parseError)
    {
        size = 0;
        parseError = $"invalid size: invalid format for size '{original}', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix";
        return false;
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

    private static readonly Encoding Utf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Encoding Utf8Strict =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
