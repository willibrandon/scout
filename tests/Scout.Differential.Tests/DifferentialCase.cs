using System;

namespace Scout;

internal sealed class DifferentialCase
{
    public DifferentialCase(DifferentialComparisonMode comparisonMode, params string[] arguments)
        : this(comparisonMode, null, null, null, arguments)
    {
    }

    private DifferentialCase(
        DifferentialComparisonMode comparisonMode,
        string? relativeWorkingDirectory,
        byte[]? standardInput,
        string? relativeConfigPath,
        params string[] arguments)
    {
        Arguments = arguments;
        ComparisonMode = comparisonMode;
        RelativeWorkingDirectory = relativeWorkingDirectory;
        StandardInput = standardInput;
        RelativeConfigPath = relativeConfigPath;
    }

    public string[] Arguments { get; }

    public DifferentialComparisonMode ComparisonMode { get; }

    public byte[]? StandardInput { get; }

    public string? RelativeConfigPath { get; }

    public string? RelativeWorkingDirectory { get; }

    public static DifferentialCase Exact(params string[] arguments)
    {
        return new DifferentialCase(DifferentialComparisonMode.Exact, arguments);
    }

    public static DifferentialCase ExactInDirectory(string relativeWorkingDirectory, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeWorkingDirectory);

        return new DifferentialCase(DifferentialComparisonMode.Exact, relativeWorkingDirectory, null, null, arguments);
    }

    public static DifferentialCase ExactWithStandardInput(byte[] standardInput, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(standardInput);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, standardInput, null, arguments);
    }

    public static DifferentialCase ExactWithConfig(string relativeConfigPath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativeConfigPath);

        return new DifferentialCase(DifferentialComparisonMode.Exact, null, null, relativeConfigPath, arguments);
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

        return new DifferentialCase(comparisonMode, relativeWorkingDirectory, null, null, arguments);
    }
}
