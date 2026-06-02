using System;
using System.IO;

namespace Scout;

internal static class SearchFileContentReader
{
    public static bool TryRead(
        string path,
        CliLowArgs lowArgs,
        bool autoMmapEligible,
        DiagnosticMessenger diagnostics,
        out byte[] bytes,
        out SearchFileReadKind readKind,
        long? knownLength = null)
    {
        readKind = SearchFileReadKind.Buffered;
        if (!TryReadPreprocessedBytes(path, lowArgs, diagnostics, out bytes, out bool handled))
        {
            return false;
        }

        if (handled)
        {
            bytes = ApplySearchEncoding(bytes, lowArgs.EncodingMode);
            return true;
        }

        try
        {
            SearchFileReadResult result = SearchFileReader.Read(
                path,
                ToSearchEncodingKind(lowArgs.EncodingMode),
                ToSearchMmapMode(lowArgs.MmapMode),
                autoMmapEligible,
                knownLength);
            bytes = result.GetBytes();
            readKind = result.Kind;
            return true;
        }
        catch (IOException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            bytes = [];
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path}: {exception.Message}").WithContext($"rg: {path}"));
            bytes = [];
            return false;
        }
    }

    public static bool TryReadRawUnix(
        SearchPathArgument path,
        CliLowArgs lowArgs,
        DiagnosticMessenger diagnostics,
        out byte[] bytes,
        out SearchFileReadKind readKind)
    {
        readKind = SearchFileReadKind.Buffered;
        try
        {
            SearchFileReadResult result = SearchFileReader.ReadUnixPath(path.UnixBytes!, ToSearchEncodingKind(lowArgs.EncodingMode));
            bytes = result.GetBytes();
            readKind = result.Kind;
            return true;
        }
        catch (IOException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path.DisplayText}: {exception.Message}").WithContext($"rg: {path.DisplayText}"));
            bytes = [];
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path.DisplayText}: {exception.Message}").WithContext($"rg: {path.DisplayText}"));
            bytes = [];
            return false;
        }
        catch (ArgumentException exception)
        {
            SearchErrorMessage(lowArgs, diagnostics, new ScoutError($"IO error for operation on {path.DisplayText}: {exception.Message}").WithContext($"rg: {path.DisplayText}"));
            bytes = [];
            return false;
        }
    }

    public static byte[] ReadSearchStream(Stream stream, CliEncodingMode encodingMode)
    {
        return SearchEncodingReader.ReadToEnd(stream, ToSearchEncodingKind(encodingMode));
    }

    private static byte[] ApplySearchEncoding(byte[] bytes, CliEncodingMode encodingMode)
    {
        return SearchEncoding.Decode(bytes, ToSearchEncodingKind(encodingMode));
    }

    private static bool TryReadPreprocessedBytes(
        string path,
        CliLowArgs lowArgs,
        DiagnosticMessenger diagnostics,
        out byte[] bytes,
        out bool handled)
    {
        bytes = [];
        handled = false;
        if (ShouldRunPreprocessor(path, lowArgs))
        {
            handled = true;
            if (CliSearchCommandRunner.TryRun(path, lowArgs.Preprocessor!, [path], pipeFileToStandardInput: true, fallbackOnStartError: false, out bytes, out ScoutError? error))
            {
                return true;
            }

            SearchErrorMessage(lowArgs, diagnostics, error!.WithContext($"rg: {path}"));
            return false;
        }

        if (!lowArgs.SearchZip || !TryGetDecompressionCommand(path, out string program, out string[] arguments))
        {
            return true;
        }

        if (CliSearchCommandRunner.TryRun(path, program, arguments, pipeFileToStandardInput: false, fallbackOnStartError: true, out bytes, out ScoutError? decompressionError))
        {
            handled = true;
            return true;
        }

        if (decompressionError is not null)
        {
            handled = true;
            SearchErrorMessage(lowArgs, diagnostics, decompressionError.WithContext($"rg: {path}"));
            return false;
        }

        return true;
    }

    private static bool ShouldRunPreprocessor(string path, CliLowArgs lowArgs)
    {
        if (lowArgs.Preprocessor is null)
        {
            return false;
        }

        if (lowArgs.PreprocessorGlobs.Count == 0)
        {
            return true;
        }

        string fullPath = Path.GetFullPath(path);
        string baseDirectory = Path.GetPathRoot(fullPath) ?? Directory.GetCurrentDirectory();
        var builder = new OverrideBuilder(baseDirectory);
        for (int index = 0; index < lowArgs.PreprocessorGlobs.Count; index++)
        {
            builder.Add(lowArgs.PreprocessorGlobs[index]);
        }

        Override matcher = builder.Build();
        return !matcher.IsIgnored(fullPath, isDirectory: false);
    }

    private static bool TryGetDecompressionCommand(string path, out string program, out string[] arguments)
    {
        if (CliDecompressionMatcher.TryGetAvailableDefaultCommand(path, out CliDecompressionCommand? command) &&
            command is not null)
        {
            program = command.Program;
            arguments = command.CreateArguments(path);
            return true;
        }

        program = string.Empty;
        arguments = [];
        return false;
    }

    private static SearchEncodingKind ToSearchEncodingKind(CliEncodingMode encodingMode)
    {
        return encodingMode switch
        {
            CliEncodingMode.None => SearchEncodingKind.None,
            CliEncodingMode.Utf8 => SearchEncodingKind.Utf8,
            CliEncodingMode.Utf16 => SearchEncodingKind.Utf16,
            CliEncodingMode.Utf16Le => SearchEncodingKind.Utf16Le,
            CliEncodingMode.Utf16Be => SearchEncodingKind.Utf16Be,
            CliEncodingMode.EucKr => SearchEncodingKind.EucKr,
            CliEncodingMode.EucJp => SearchEncodingKind.EucJp,
            CliEncodingMode.Big5 => SearchEncodingKind.Big5,
            CliEncodingMode.Gb18030 => SearchEncodingKind.Gb18030,
            CliEncodingMode.Gbk => SearchEncodingKind.Gbk,
            CliEncodingMode.ShiftJis => SearchEncodingKind.ShiftJis,
            CliEncodingMode.Ibm866 => SearchEncodingKind.Ibm866,
            CliEncodingMode.Iso88592 => SearchEncodingKind.Iso88592,
            CliEncodingMode.Iso88593 => SearchEncodingKind.Iso88593,
            CliEncodingMode.Iso88594 => SearchEncodingKind.Iso88594,
            CliEncodingMode.Iso88595 => SearchEncodingKind.Iso88595,
            CliEncodingMode.Iso88596 => SearchEncodingKind.Iso88596,
            CliEncodingMode.Iso88597 => SearchEncodingKind.Iso88597,
            CliEncodingMode.Iso88598 => SearchEncodingKind.Iso88598,
            CliEncodingMode.Iso88598I => SearchEncodingKind.Iso88598I,
            CliEncodingMode.Iso885910 => SearchEncodingKind.Iso885910,
            CliEncodingMode.Iso885913 => SearchEncodingKind.Iso885913,
            CliEncodingMode.Iso885914 => SearchEncodingKind.Iso885914,
            CliEncodingMode.Iso885915 => SearchEncodingKind.Iso885915,
            CliEncodingMode.Iso885916 => SearchEncodingKind.Iso885916,
            CliEncodingMode.Iso2022Jp => SearchEncodingKind.Iso2022Jp,
            CliEncodingMode.Koi8R => SearchEncodingKind.Koi8R,
            CliEncodingMode.Koi8U => SearchEncodingKind.Koi8U,
            CliEncodingMode.Macintosh => SearchEncodingKind.Macintosh,
            CliEncodingMode.Windows874 => SearchEncodingKind.Windows874,
            CliEncodingMode.Windows1250 => SearchEncodingKind.Windows1250,
            CliEncodingMode.Windows1251 => SearchEncodingKind.Windows1251,
            CliEncodingMode.Windows1252 => SearchEncodingKind.Windows1252,
            CliEncodingMode.Windows1253 => SearchEncodingKind.Windows1253,
            CliEncodingMode.Windows1254 => SearchEncodingKind.Windows1254,
            CliEncodingMode.Windows1255 => SearchEncodingKind.Windows1255,
            CliEncodingMode.Windows1256 => SearchEncodingKind.Windows1256,
            CliEncodingMode.Windows1257 => SearchEncodingKind.Windows1257,
            CliEncodingMode.Windows1258 => SearchEncodingKind.Windows1258,
            CliEncodingMode.XMacCyrillic => SearchEncodingKind.XMacCyrillic,
            CliEncodingMode.XUserDefined => SearchEncodingKind.XUserDefined,
            _ => SearchEncodingKind.Auto,
        };
    }

    private static SearchMmapMode ToSearchMmapMode(CliMmapMode mmapMode)
    {
        return mmapMode switch
        {
            CliMmapMode.AlwaysTryMmap => SearchMmapMode.AlwaysTryMmap,
            CliMmapMode.Never => SearchMmapMode.Never,
            _ => SearchMmapMode.Auto,
        };
    }

    private static void SearchErrorMessage(CliLowArgs lowArgs, DiagnosticMessenger diagnostics, ScoutError error)
    {
        if (lowArgs.Messages)
        {
            diagnostics.ErrorMessage(error);
            return;
        }

        diagnostics.MarkErrored();
    }
}
