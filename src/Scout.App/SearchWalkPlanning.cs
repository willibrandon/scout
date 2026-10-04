using System.Text;

namespace Scout;

/// <summary>
/// Plans directory walks and their search parallelism.
/// </summary>
internal static class SearchWalkPlanning
{
    /// <summary>
    /// Gets the maximum number of ordered workers used to search segments of one large file.
    /// </summary>
    internal const int MaximumLargeFileSegmentWorkerCount = 3;

    private const int MacOsDefaultSearchWalkThreadCount = 3;
    private const int MacOsDefaultReplacementSearchWalkThreadCount = 6;
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static int RunTypeList(CliLowArgs lowArgs, RawByteWriter output, DiagnosticMessenger diagnostics)
    {
        if (!TryBuildFileTypeMatcher(lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error))
        {
            diagnostics.ErrorMessage(error!.WithContext(ScoutErrorContext.ProgramContext()));
            return ExitCode.Error;
        }

        foreach (FileTypeDefinition definition in fileTypes!.Definitions)
        {
            output.Write(s_utf8.GetBytes(definition.Name));
            output.Write(": "u8);
            for (int index = 0; index < definition.Globs.Count; index++)
            {
                if (index > 0)
                {
                    output.Write(", "u8);
                }

                output.Write(s_utf8.GetBytes(definition.Globs[index]));
            }

            output.Write("\n"u8);
        }

        output.Flush();
        return ExitCode.Success;
    }

    internal static WalkBuilder CreateWalkBuilder(string path, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger, Action onError)
    {
        WalkBuilder builder = new WalkBuilder(path)
            .Diagnostics(logger)
            .ErrorHandler(error =>
            {
                onError();
                string errorPath = error.Path.IsWindowsText
                    ? error.Path.AsWindowsString()
                    : Encoding.UTF8.GetString(error.Path.AsUnixBytes());
                string detail = error.InnerException?.InnerException is System.ComponentModel.Win32Exception native
                    ? $"{native.Message} (os error {native.NativeErrorCode})"
                    : error.Message;
                var cause = new ScoutError($"IO error for operation on {errorPath}: {detail}");
                SearchApplicationDiagnostics.ReportError(lowArgs, diagnostics,
                    cause.WithContext(ScoutErrorContext.ProgramPathContext(errorPath)));
                return WalkState.Continue;
            })
            .Hidden(!lowArgs.IncludeHidden)
            .FollowLinks(lowArgs.FollowLinks)
            .SameFileSystem(lowArgs.OneFileSystem)
            .MaxDepth(GetWalkMaxDepth(lowArgs.MaxDepth))
            .MaxFileSize(GetWalkMaxFileSize(lowArgs.MaxFileSize))
            .Overrides(BuildOverrides(lowArgs))
            .FileTypes(fileTypes)
            .Ignore(lowArgs.RespectDotIgnoreFiles)
            .GitIgnore(lowArgs.RespectGitIgnoreFiles)
            .GitExclude(lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGitExcludeFiles)
            .GitGlobal(lowArgs.RespectGitIgnoreFiles && lowArgs.RespectGlobalIgnoreFiles)
            .Parents(lowArgs.RespectParentIgnoreFiles)
            .RequireGit(lowArgs.RequireGitRepository)
            .IgnoreCaseInsensitive(lowArgs.IgnoreFileCaseInsensitive);
        if (lowArgs.RespectExplicitIgnoreFiles)
        {
            for (int index = 0; index < lowArgs.IgnoreFiles.Count; index++)
            {
                if (!builder.TryAddIgnoreFile(lowArgs.IgnoreFiles[index], out string? errorMessage) && lowArgs.Messages)
                {
                    diagnostics.ErrorMessage(new ScoutError(errorMessage!).WithContext(ScoutErrorContext.ProgramContext()));
                }
            }
        }

        if (lowArgs.SortMode is { Reverse: false, Kind: CliSortKind.Path })
        {
            builder.SortByFileName();
        }

        return builder;
    }

    internal static List<DirEntry> GetSortedFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger, out bool errored)
    {
        var errors = new DiagnosticState();
        int threadCount = GetDirectoryWalkThreadCount(lowArgs);
        List<DirEntry> entries = threadCount > 1
            ? GetParallelFileEntries(root, lowArgs, fileTypes, diagnostics, logger, threadCount, errors)
            : GetSerialFileEntries(root, lowArgs, fileTypes, diagnostics, logger, errors);
        SortFileEntries(entries, lowArgs.SortMode);
        errored = errors.HasErrored;
        return entries;
    }

    internal static int GetFilesWalkThreadCount(CliLowArgs lowArgs)
    {
        ulong resolvedThreads = SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        return resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
    }

    internal static int GetSearchWalkThreadCount(CliLowArgs lowArgs)
    {
        ulong resolvedThreads = SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        int threadCount = resolvedThreads > int.MaxValue
            ? int.MaxValue
            : (int)resolvedThreads;
        if (lowArgs.Threads is null && OperatingSystem.IsMacOS())
        {
            return GetMacOsDefaultSearchWalkThreadCount(
                threadCount,
                lowArgs.Replacement is not null);
        }

        return threadCount;
    }

    /// <summary>
    /// Gets the internal segment-worker count for a large-file search.
    /// </summary>
    /// <param name="lowArgs">The parsed search arguments.</param>
    /// <param name="allowSegmentParallelism">
    /// Whether the large file may use internal ordered segment workers.
    /// </param>
    /// <returns>The number of ordered segment workers to use.</returns>
    internal static int GetLargeFileSearchThreadCount(
        CliLowArgs lowArgs,
        bool allowSegmentParallelism)
    {
        if (!allowSegmentParallelism)
        {
            return 1;
        }

        // With no outer file-level workers, ordered segment workers may use the requested
        // search-wide thread budget.
        ulong resolvedThreads = SearchThreadPlanner.Resolve(lowArgs.Threads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        int threadCount = resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
        if (lowArgs.Threads is null && OperatingSystem.IsMacOS())
        {
            return GetMacOsDefaultLargeFileSearchThreadCount(threadCount);
        }

        return Math.Min(threadCount, MaximumLargeFileSegmentWorkerCount);
    }

    /// <summary>
    /// Bounds the default macOS large-file worker count to the ordered segment limit.
    /// </summary>
    /// <param name="threadCount">The platform-neutral search-wide thread count.</param>
    /// <returns>The number of ordered segment workers to use.</returns>
    internal static int GetMacOsDefaultLargeFileSearchThreadCount(int threadCount)
    {
        return Math.Min(threadCount, MaximumLargeFileSegmentWorkerCount);
    }

    internal static int GetMacOsDefaultSearchWalkThreadCount(
        int threadCount,
        bool replacement)
    {
        if (threadCount <= 1)
        {
            return 1;
        }

        int maximumThreadCount = replacement
            ? MacOsDefaultReplacementSearchWalkThreadCount
            : MacOsDefaultSearchWalkThreadCount;
        return Math.Min(threadCount, maximumThreadCount);
    }

    internal static bool TryBuildFileTypeMatcher(CliLowArgs lowArgs, out FileTypeMatcher? fileTypes, out ScoutError? error)
    {
        FileTypeMatcherBuilder builder = new FileTypeMatcherBuilder().AddDefaults();
        try
        {
            for (int index = 0; index < lowArgs.TypeChanges.Count; index++)
            {
                CliTypeChange change = lowArgs.TypeChanges[index];
                ApplyTypeChange(builder, change);
            }

            fileTypes = builder.Build();
            error = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            fileTypes = null;
            error = new ScoutError(exception.Message);
            return false;
        }
        catch (ArgumentException)
        {
            fileTypes = null;
            error = new ScoutError("invalid definition (format is type:glob, e.g., html:*.html)");
            return false;
        }
    }

    internal static bool TryValidateOverrideGlobs(CliLowArgs lowArgs, DiagnosticMessenger diagnostics)
    {
        var builder = new OverrideBuilder(Directory.GetCurrentDirectory());
        for (int index = 0; index < lowArgs.GlobPatterns.Count; index++)
        {
            CliGlobPattern pattern = lowArgs.GlobPatterns[index];
            try
            {
                builder.Add(pattern.Value, pattern.CaseInsensitive || lowArgs.GlobCaseInsensitive);
            }
            catch (GlobParseException exception)
            {
                diagnostics.ErrorMessage(new ScoutError($"error parsing glob '{pattern.Value}': {exception.Message}").WithContext(ScoutErrorContext.ProgramContext()));
                return false;
            }
        }

        return true;
    }

    private static List<DirEntry> GetSerialFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger, DiagnosticState errors)
    {
        return CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger, errors.SetErrored).Build()
            .Where(static entry => entry.IsFile)
            .ToList();
    }

    private static List<DirEntry> GetParallelFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger, int threadCount, DiagnosticState errors)
    {
        List<DirEntry> entries = [];
        object entriesLock = new();
        CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger, errors.SetErrored).Threads(threadCount).BuildParallel().Run(() => entry =>
        {
            if (entry.IsFile)
            {
                lock (entriesLock)
                {
                    entries.Add(entry);
                }
            }

            return WalkState.Continue;
        });

        return entries;
    }

    private static int GetDirectoryWalkThreadCount(CliLowArgs lowArgs)
    {
        if (lowArgs.Threads is not ulong requestedThreads || requestedThreads <= 1)
        {
            return 1;
        }

        ulong resolvedThreads = SearchThreadPlanner.Resolve(requestedThreads, lowArgs.SortMode is not null, isOneFile: false);
        if (resolvedThreads <= 1)
        {
            return 1;
        }

        return resolvedThreads > int.MaxValue ? int.MaxValue : (int)resolvedThreads;
    }

    private static void ApplyTypeChange(FileTypeMatcherBuilder builder, CliTypeChange change)
    {
        switch (change.Kind)
        {
            case CliTypeChangeKind.Select:
                builder.Select(change.Value);
                break;

            case CliTypeChangeKind.Negate:
                builder.Negate(change.Value);
                break;

            case CliTypeChangeKind.Add:
                builder.AddDefinition(change.Value);
                break;

            case CliTypeChangeKind.Clear:
                builder.Clear(change.Value);
                break;
        }
    }

    private static void SortFileEntries(List<DirEntry> entries, CliSortMode? sortMode)
    {
        if (sortMode is null || sortMode.Value is { Reverse: false, Kind: CliSortKind.Path })
        {
            return;
        }

        CliSortMode mode = sortMode.Value;
        if (mode.Kind == CliSortKind.Path)
        {
            entries.Sort((left, right) => ComparePath(left, right, mode.Reverse));
            return;
        }

        entries.Sort((left, right) => CompareTime(left, right, mode));
    }

    private static int ComparePath(DirEntry left, DirEntry right, bool reverse)
    {
        int comparison = left.IsRawUnixPath && right.IsRawUnixPath
            ? left.UnixPathBytes.SequenceCompareTo(right.UnixPathBytes)
            : StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        return reverse ? -comparison : comparison;
    }

    private static int CompareTime(DirEntry left, DirEntry right, CliSortMode mode)
    {
        DateTime? leftTime = left.IsRawUnixPath ? null : GetSortTime(left.FullPath, mode.Kind);
        DateTime? rightTime = right.IsRawUnixPath ? null : GetSortTime(right.FullPath, mode.Kind);
        int comparison = CompareNullableTime(leftTime, rightTime);
        return mode.Reverse ? -comparison : comparison;
    }

    private static int CompareNullableTime(DateTime? left, DateTime? right)
    {
        if (left.HasValue && right.HasValue)
        {
            return left.Value.CompareTo(right.Value);
        }

        if (left.HasValue)
        {
            return -1;
        }

        return right.HasValue ? 1 : 0;
    }

    private static DateTime? GetSortTime(string path, CliSortKind kind)
    {
        try
        {
            var info = new FileInfo(path);
            return kind switch
            {
                CliSortKind.LastModified => info.LastWriteTimeUtc,
                CliSortKind.LastAccessed => info.LastAccessTimeUtc,
                CliSortKind.Created => info.CreationTimeUtc,
                _ => null,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? GetWalkMaxDepth(ulong? maxDepth)
    {
        if (maxDepth is null)
        {
            return null;
        }

        return maxDepth.Value > int.MaxValue ? int.MaxValue : (int)maxDepth.Value;
    }

    private static long? GetWalkMaxFileSize(ulong? maxFileSize)
    {
        if (maxFileSize is null)
        {
            return null;
        }

        return maxFileSize.Value > long.MaxValue ? long.MaxValue : (long)maxFileSize.Value;
    }

    private static Override BuildOverrides(CliLowArgs lowArgs)
    {
        if (lowArgs.GlobPatterns.Count == 0)
        {
            return Override.Empty;
        }

        var builder = new OverrideBuilder(Directory.GetCurrentDirectory());
        for (int index = 0; index < lowArgs.GlobPatterns.Count; index++)
        {
            CliGlobPattern pattern = lowArgs.GlobPatterns[index];
            builder.Add(pattern.Value, pattern.CaseInsensitive || lowArgs.GlobCaseInsensitive);
        }

        return builder.Build();
    }
}
