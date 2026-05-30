using System;
using System.IO;

namespace Scout;

internal sealed class DifferentialCase
{
    public DifferentialCase(DifferentialComparisonMode comparisonMode, params string[] arguments)
        : this(comparisonMode, null, null, null, null, null, arguments)
    {
    }

    private DifferentialCase(
        DifferentialComparisonMode comparisonMode,
        string? relativeWorkingDirectory,
        byte[]? standardInput,
        string? relativeConfigPath,
        Action<RgTestDirectory>? beforeRun,
        Func<RgTestDirectory, string[]>? argumentFactory,
        params string[] arguments)
    {
        DifferentialComparisonMode resolvedComparisonMode = ResolveComparisonMode(comparisonMode, arguments, workingDirectory: null);
        ValidateComparisonPolicy(resolvedComparisonMode, arguments);

        this.arguments = arguments;
        this.argumentFactory = argumentFactory;
        requestedComparisonMode = comparisonMode;
        BeforeRun = beforeRun;
        ComparisonMode = resolvedComparisonMode;
        RelativeWorkingDirectory = relativeWorkingDirectory;
        StandardInput = standardInput;
        RelativeConfigPath = relativeConfigPath;
    }

    private readonly string[]? arguments;
    private readonly Func<RgTestDirectory, string[]>? argumentFactory;
    private readonly DifferentialComparisonMode requestedComparisonMode;

    public string[] Arguments => arguments ?? throw new InvalidOperationException("This differential case uses directory-specific arguments.");

    public Action<RgTestDirectory>? BeforeRun { get; }

    public DifferentialComparisonMode ComparisonMode { get; }

    public byte[]? StandardInput { get; }

    public string? RelativeConfigPath { get; }

    public string? RelativeWorkingDirectory { get; }

    public static DifferentialCase Exact(params string[] arguments)
    {
        return new DifferentialCase(DifferentialComparisonMode.Exact, arguments);
    }

    public static DifferentialCase ExactWithArguments(Func<RgTestDirectory, string[]> argumentFactory)
    {
        ArgumentNullException.ThrowIfNull(argumentFactory);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, null, null, null, argumentFactory);
    }

    public static DifferentialCase ExactInDirectory(string relativeWorkingDirectory, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeWorkingDirectory);

        return new DifferentialCase(DifferentialComparisonMode.Exact, relativeWorkingDirectory, null, null, null, null, arguments);
    }

    public static DifferentialCase ExactInDirectoryWithArguments(string relativeWorkingDirectory, Func<RgTestDirectory, string[]> argumentFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeWorkingDirectory);
        ArgumentNullException.ThrowIfNull(argumentFactory);

        return new DifferentialCase(DifferentialComparisonMode.Exact, relativeWorkingDirectory, null, null, null, argumentFactory);
    }

    public static DifferentialCase ExactWithSetup(Action<RgTestDirectory> beforeRun, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(beforeRun);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, null, null, beforeRun, null, arguments);
    }

    public static DifferentialCase ExactWithStandardInput(byte[] standardInput, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(standardInput);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, standardInput, null, null, null, arguments);
    }

    public static DifferentialCase ExactWithConfig(string relativeConfigPath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeConfigPath);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, null, relativeConfigPath, null, null, arguments);
    }

    public static DifferentialCase Normalized(DifferentialComparisonMode comparisonMode, params string[] arguments)
    {
        return NormalizedInDirectory(comparisonMode, null, arguments);
    }

    public static DifferentialCase NormalizedInDirectory(DifferentialComparisonMode comparisonMode, string? relativeWorkingDirectory, params string[] arguments)
    {
        if (comparisonMode == DifferentialComparisonMode.Exact)
        {
            throw new ArgumentException("Use Exact for exact differential cases.", nameof(comparisonMode));
        }

        return new DifferentialCase(comparisonMode, relativeWorkingDirectory, null, null, null, null, arguments);
    }

    public string[] GetArguments(RgTestDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string[] resolvedArguments = argumentFactory is null ? Arguments : argumentFactory(directory);
        ValidateComparisonPolicy(ResolveComparisonMode(requestedComparisonMode, resolvedArguments, GetWorkingDirectory(directory.RootPath)), resolvedArguments);
        return resolvedArguments;
    }

    public DifferentialComparisonMode GetComparisonMode(string[] resolvedArguments, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(resolvedArguments);

        DifferentialComparisonMode comparisonMode = ResolveComparisonMode(requestedComparisonMode, resolvedArguments, workingDirectory);
        ValidateComparisonPolicy(comparisonMode, resolvedArguments);
        return comparisonMode;
    }

    private static DifferentialComparisonMode ResolveComparisonMode(DifferentialComparisonMode comparisonMode, string[] arguments, string? workingDirectory)
    {
        if (!UsesDefaultParallelTraversal(arguments, workingDirectory))
        {
            return comparisonMode;
        }

        return comparisonMode switch
        {
            DifferentialComparisonMode.Exact => DifferentialComparisonMode.SortLines,
            DifferentialComparisonMode.MaskElapsed => DifferentialComparisonMode.SortLinesAndMaskElapsed,
            _ => comparisonMode,
        };
    }

    private static void ValidateComparisonPolicy(DifferentialComparisonMode comparisonMode, string[] arguments)
    {
        if (UsesExplicitParallelism(arguments) && !NormalizesPathOrder(comparisonMode))
        {
            throw new ArgumentException("Explicitly parallel differential comparisons must use a path-order-normalizing comparison mode.", nameof(comparisonMode));
        }

        if (!ProducesElapsedFields(arguments))
        {
            return;
        }

        if (!MasksElapsedFields(comparisonMode))
        {
            throw new ArgumentException("Differential cases that emit JSON or stats must use an elapsed-masking comparison mode.", nameof(comparisonMode));
        }

        if (UsesExplicitParallelism(arguments) && comparisonMode == DifferentialComparisonMode.MaskElapsed)
        {
            throw new ArgumentException("Explicitly parallel JSON or stats comparisons must also sort path-ordered output.", nameof(comparisonMode));
        }
    }

    private static bool MasksElapsedFields(DifferentialComparisonMode comparisonMode)
    {
        return comparisonMode is DifferentialComparisonMode.MaskElapsed
            or DifferentialComparisonMode.SortLinesAndMaskElapsed
            or DifferentialComparisonMode.NonEmptyStdout
            or DifferentialComparisonMode.NonEmptyStderr;
    }

    private static bool NormalizesPathOrder(DifferentialComparisonMode comparisonMode)
    {
        return comparisonMode is DifferentialComparisonMode.SortLines
            or DifferentialComparisonMode.SortLinesAndMaskElapsed
            or DifferentialComparisonMode.NonEmptyStdout
            or DifferentialComparisonMode.NonEmptyStderr;
    }

    private static bool ProducesElapsedFields(string[] arguments)
    {
        if (TryParse(arguments, out CliLowArgs? lowArgs))
        {
            CliLowArgs parsed = lowArgs!;
            return parsed.SearchMode == CliSearchMode.Json || parsed.Stats;
        }

        return ProducesElapsedFieldsFromTokens(arguments);
    }

    private static bool ProducesElapsedFieldsFromTokens(string[] arguments)
    {
        bool json = false;
        bool stats = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--json":
                    json = true;
                    break;
                case "--no-json":
                    json = false;
                    break;
                case "--stats":
                    stats = true;
                    break;
                case "--no-stats":
                    stats = false;
                    break;
            }
        }

        return json || stats;
    }

    private static bool UsesExplicitParallelism(string[] arguments)
    {
        if (UsesSortedTraversal(arguments))
        {
            return false;
        }

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith("-j", StringComparison.Ordinal) && argument.Length > 2)
            {
                return IsParallelThreadCount(argument[2..]);
            }

            if (argument == "-j" || argument == "--threads")
            {
                return index + 1 < arguments.Length && IsParallelThreadCount(arguments[index + 1]);
            }

            const string threadsPrefix = "--threads=";
            if (argument.StartsWith(threadsPrefix, StringComparison.Ordinal))
            {
                return IsParallelThreadCount(argument[threadsPrefix.Length..]);
            }
        }

        return false;
    }

    private static bool UsesSortedTraversal(string[] arguments)
    {
        return TryParse(arguments, out CliLowArgs? lowArgs) && lowArgs!.SortMode is not null;
    }

    private static bool IsParallelThreadCount(string value)
    {
        return ulong.TryParse(value, out ulong threads) && threads != 1;
    }

    private static bool UsesDefaultParallelTraversal(string[] arguments, string? workingDirectory)
    {
        if (!TryParse(arguments, out CliLowArgs? lowArgs))
        {
            return false;
        }

        CliLowArgs parsed = lowArgs!;
        if (parsed.Threads is not null)
        {
            return false;
        }

        if (parsed.SortMode is not null)
        {
            return false;
        }

        if (parsed.SearchMode == CliSearchMode.Files)
        {
            return true;
        }

        int firstPathIndex = GetFirstPathIndex(parsed);
        int searchedPathCount = parsed.Positional.Count - firstPathIndex;
        if (searchedPathCount > 1)
        {
            return true;
        }

        for (int index = firstPathIndex; index < parsed.Positional.Count; index++)
        {
            if (!parsed.Positional[index].TryGetText(out string path))
            {
                continue;
            }

            if (IsDirectoryPath(path, workingDirectory))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParse(string[] arguments, out CliLowArgs? lowArgs)
    {
        var osArguments = new OsString[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index] = OsString.FromText(arguments[index]);
        }

        CliParseResult result = CliParser.Parse(osArguments);
        lowArgs = result.LowArgs;
        return result.Status == CliParseStatus.Ok && lowArgs is not null;
    }

    private static int GetFirstPathIndex(CliLowArgs lowArgs)
    {
        if (lowArgs.SearchMode == CliSearchMode.Files || lowArgs.PatternSources.Count > 0)
        {
            return 0;
        }

        return lowArgs.Positional.Count == 0 ? 0 : 1;
    }

    private static bool IsDirectoryPath(string path, string? workingDirectory)
    {
        if (path == "." || path == "./")
        {
            return true;
        }

        string fullPath = workingDirectory is null || Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(workingDirectory, path);
        return Directory.Exists(fullPath);
    }

    private string GetWorkingDirectory(string rootPath)
    {
        return RelativeWorkingDirectory is null ? rootPath : Path.Combine(rootPath, RelativeWorkingDirectory);
    }
}
