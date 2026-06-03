using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Scout;

internal static class FileListingOperations
{
    private static readonly byte[] StandardInputPath = "<stdin>"u8.ToArray();

    internal static int Run(
        IReadOnlyList<OsString> positional,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics)
    {
        bool emitted = false;
        bool errored = false;
        if (positional.Count == 0)
        {
            ListPath(path: ".", defaultRoot: true, lowArgs, fileTypes, output, diagnostics, ref emitted, ref errored);
        }
        else
        {
            for (int index = 0; index < positional.Count; index++)
            {
                if (SearchPathArgument.TryGetText(positional[index], diagnostics, out string path))
                {
                    ListPath(path, defaultRoot: false, lowArgs, fileTypes, output, diagnostics, ref emitted, ref errored);
                }
                else
                {
                    errored = true;
                }
            }
        }

        output.Flush();
        return SearchOutputFormatting.GetSearchExitCode(emitted, errored, lowArgs.Quiet);
    }

    private static void ListPath(
        string path,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted,
        ref bool errored)
    {
        if (path == "-")
        {
            if (!lowArgs.Quiet)
            {
                output.Write(StandardInputPath);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
            return;
        }

        if (File.Exists(path))
        {
            if (!lowArgs.Quiet)
            {
                output.Write(SearchPathArgument.GetPathBytes(path, lowArgs.PathSeparator));
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
            return;
        }

        if (!Directory.Exists(path))
        {
            SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics, SearchApplicationDiagnostics.MissingPath(path));
            errored = true;
            return;
        }

        string fullRoot = Path.GetFullPath(path);
        int threadCount = SearchWalkPlanning.GetFilesWalkThreadCount(lowArgs);
        if (threadCount > 1)
        {
            ListDirectoryParallel(path, fullRoot, defaultRoot, lowArgs, fileTypes, output, diagnostics, ref emitted);
            return;
        }

        ListDirectorySerial(path, fullRoot, defaultRoot, lowArgs, fileTypes, output, diagnostics, ref emitted);
    }

    private static void ListDirectorySerial(
        string path,
        string fullRoot,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted)
    {
        foreach (DirEntry entry in SearchWalkPlanning.GetSortedFileEntries(path, lowArgs, fileTypes, diagnostics))
        {
            string displayPath = defaultRoot
                ? SearchPathArgument.GetSearchDirectoryDisplayPath(path, fullRoot, entry.FullPath, defaultRoot: true)
                : SearchPathArgument.GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
            byte[] displayPathBytes = entry.IsRawUnixPath
                ? SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, lowArgs.PathSeparator)
                : SearchPathArgument.GetPathBytes(displayPath, lowArgs.PathSeparator);
            if (!lowArgs.Quiet)
            {
                output.Write(displayPathBytes);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }

            emitted = true;
        }
    }

    private static void ListDirectoryParallel(
        string path,
        string fullRoot,
        bool defaultRoot,
        CliLowArgs lowArgs,
        FileTypeMatcher fileTypes,
        RawByteWriter output,
        DiagnosticMessenger diagnostics,
        ref bool emitted)
    {
        int threadCount = SearchWalkPlanning.GetFilesWalkThreadCount(lowArgs);
        using var entries = new BlockingCollection<DirEntry>();
        int found = 0;
        var printThread = BackgroundThread.Start(() =>
        {
            foreach (DirEntry entry in entries.GetConsumingEnumerable())
            {
                string displayPath = defaultRoot
                    ? SearchPathArgument.GetSearchDirectoryDisplayPath(path, fullRoot, entry.FullPath, defaultRoot: true)
                    : SearchPathArgument.GetDirectoryDisplayPath(path, fullRoot, entry.FullPath);
                byte[] displayPathBytes = entry.IsRawUnixPath
                    ? SearchPathArgument.GetSearchDirectoryDisplayPathBytes(path, fullRoot, entry, defaultRoot, lowArgs.PathSeparator)
                    : SearchPathArgument.GetPathBytes(displayPath, lowArgs.PathSeparator);
                output.Write(displayPathBytes);
                SearchOutputFormatting.WritePathTerminator(output, lowArgs.NullPathTerminator);
            }
        });

        try
        {
            SearchWalkPlanning.CreateWalkBuilder(path, lowArgs, fileTypes, diagnostics).Threads(threadCount).BuildParallel().Run(() => entry =>
            {
                if (!entry.IsFile)
                {
                    return WalkState.Continue;
                }

                Interlocked.Exchange(ref found, 1);
                if (lowArgs.Quiet)
                {
                    return WalkState.Quit;
                }

                entries.Add(entry);
                return WalkState.Continue;
            });
        }
        finally
        {
            entries.CompleteAdding();
        }

        printThread.Join();
        emitted |= Volatile.Read(ref found) != 0;
    }
}
