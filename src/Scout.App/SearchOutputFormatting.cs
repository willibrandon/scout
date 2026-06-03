
namespace Scout;

internal static class SearchOutputFormatting
{
    private static readonly byte[] StandardInputPath = "<stdin>"u8.ToArray();
    private static readonly byte[] NullByte = [0];

    internal static int GetSearchExitCode(bool matched, bool errored, bool quiet)
    {
        if (matched && (quiet || !errored))
        {
            return ExitCode.Success;
        }

        if (!matched && !errored)
        {
            return ExitCode.NoMatch;
        }

        return ExitCode.Error;
    }

    internal static bool EffectiveLineNumber(CliLowArgs lowArgs)
    {
        return lowArgs.LineNumber || (EffectiveColumn(lowArgs) && !lowArgs.LineNumberSpecified) || (lowArgs.Vimgrep && !lowArgs.LineNumberSpecified);
    }

    internal static bool EffectiveColumn(CliLowArgs lowArgs)
    {
        return lowArgs.Column || (lowArgs.Vimgrep && !lowArgs.ColumnSpecified);
    }

    internal static bool WriteCount(
        RawByteWriter output,
        OutputPath? prefix,
        OutputColor color,
        long count,
        bool includeZero,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        bool matched = count != 0;
        if (!matched && !includeZero)
        {
            return false;
        }

        if (prefix is not null)
        {
            prefix.WriteLabel(output, color);
            WritePrefixTerminator(output, nullPathTerminator, matchSeparator: true);
        }

        WriteNumber(output, count);
        output.Write(lineTerminator.Span);
        return matched;
    }

    internal static bool WritePathIf(
        RawByteWriter output,
        OutputPath? path,
        OutputColor color,
        bool condition,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (!condition)
        {
            return false;
        }

        if (path is null)
        {
            output.Write(StandardInputPath);
        }
        else
        {
            path.WriteLabel(output, color);
        }

        WriteSearchPathTerminator(output, nullPathTerminator, lineTerminator);
        return true;
    }

    internal static void WriteSearchPathTerminator(
        RawByteWriter output,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> lineTerminator)
    {
        if (nullPathTerminator)
        {
            output.Write(NullByte);
            return;
        }

        output.Write(lineTerminator.Span);
    }

    internal static void WritePathTerminator(RawByteWriter output, bool nullPathTerminator)
    {
        output.Write(nullPathTerminator ? NullByte : "\n"u8);
    }

    internal static OutputPath CreateOutputPath(string physicalPath, byte[] displayPath, CliLowArgs lowArgs, OutputColor color)
    {
        string? hyperlinkFormat = color.Enabled ? lowArgs.HyperlinkFormat : null;
        byte[]? hyperlinkPath = string.IsNullOrEmpty(hyperlinkFormat)
            ? null
            : OutputPath.CreateHyperlinkPath(physicalPath);
        string host = SearchDiagnosticLogging.GetHyperlinkHost(lowArgs, hyperlinkFormat);
        return new OutputPath(displayPath, hyperlinkPath, hyperlinkFormat, host);
    }

    internal static OutputPath CreateDirectoryEntryOutputPath(DirEntry entry, byte[] displayPath, CliLowArgs lowArgs, OutputColor color)
    {
        return entry.IsRawUnixPath
            ? new OutputPath(displayPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty)
            : CreateOutputPath(entry.FullPath, displayPath, lowArgs, color);
    }

    internal static OutputPath CreateRawUnixOutputPath(SearchPathArgument path)
    {
        return new OutputPath(path.DisplayBytes, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
    }

    internal static OutputPath? GetStandardInputPrefix(CliSearchMode searchMode, bool autoPrefixPath, bool? withFilename)
    {
        if (IsFileListMode(searchMode) || ShouldPrefixMatchFields(autoPrefixPath, withFilename))
        {
            return new OutputPath(StandardInputPath, hyperlinkPath: null, hyperlinkFormat: null, host: string.Empty);
        }

        return null;
    }

    internal static OutputPath? GetFileSearchPrefix(CliSearchMode searchMode, bool autoPrefixPath, bool? withFilename, OutputPath path)
    {
        return IsFileListMode(searchMode) || ShouldPrefixMatchFields(autoPrefixPath, withFilename)
            ? path
            : null;
    }

    private static void WritePrefixTerminator(RawByteWriter output, bool nullPathTerminator, bool matchSeparator)
    {
        if (nullPathTerminator)
        {
            output.Write(NullByte);
            return;
        }

        output.Write(matchSeparator ? ":"u8 : "-"u8);
    }

    private static bool IsFileListMode(CliSearchMode searchMode)
    {
        return searchMode == CliSearchMode.FilesWithMatches || searchMode == CliSearchMode.FilesWithoutMatch;
    }

    private static bool ShouldPrefixMatchFields(bool autoPrefixPath, bool? withFilename)
    {
        return withFilename ?? autoPrefixPath;
    }

    private static void WriteNumber(RawByteWriter output, long value)
    {
        Span<byte> buffer = stackalloc byte[20];
        ulong number = (ulong)value;
        int index = buffer.Length;
        do
        {
            index--;
            buffer[index] = (byte)((number % 10) + (byte)'0');
            number /= 10;
        }
        while (number != 0);

        output.Write(buffer[index..]);
    }
}
