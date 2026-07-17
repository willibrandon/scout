using System.Text;

namespace Scout;

/// <summary>
/// Plans directory walks and their search parallelism.
/// </summary>
internal static class SearchWalkPlanning
{
    private const int MacOsDefaultSearchWalkThreadCount = 3;
    private const int MacOsDefaultReplacementSearchWalkThreadCount = 6;
    private const int MacOsDefaultLargeFileSearchThreadCount = 4;
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

    internal static WalkBuilder CreateWalkBuilder(string path, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger)
    {
        WalkBuilder builder = new WalkBuilder(path)
            .Diagnostics(logger)
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

    internal static List<DirEntry> GetSortedFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger)
    {
        int threadCount = GetDirectoryWalkThreadCount(lowArgs);
        List<DirEntry> entries = threadCount > 1
            ? GetParallelFileEntries(root, lowArgs, fileTypes, diagnostics, logger, threadCount)
            : GetSerialFileEntries(root, lowArgs, fileTypes, diagnostics, logger);
        SortFileEntries(entries, lowArgs.SortMode);
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

        return threadCount;
    }

    internal static int GetMacOsDefaultLargeFileSearchThreadCount(int threadCount)
    {
        return Math.Min(threadCount, MacOsDefaultLargeFileSearchThreadCount);
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

    private static List<DirEntry> GetSerialFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger)
    {
        List<DirEntry> entries = [];
        foreach (DirEntry entry in CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Build())
        {
            if (entry.IsFile)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static List<DirEntry> GetParallelFileEntries(string root, CliLowArgs lowArgs, FileTypeMatcher fileTypes, DiagnosticMessenger diagnostics, DiagnosticLogger logger, int threadCount)
    {
        List<DirEntry> entries = [];
        object entriesLock = new();
        CreateWalkBuilder(root, lowArgs, fileTypes, diagnostics, logger).Threads(threadCount).BuildParallel().Run(() => entry =>
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
