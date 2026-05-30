using System;

namespace Scout;

internal sealed class DifferentialCase
{
    public DifferentialCase(DifferentialComparisonMode comparisonMode, params string[] arguments)
        : this(comparisonMode, null, arguments)
    {
    }

    private DifferentialCase(DifferentialComparisonMode comparisonMode, byte[]? standardInput, params string[] arguments)
    {
        Arguments = arguments;
        ComparisonMode = comparisonMode;
        StandardInput = standardInput;
    }

    public string[] Arguments { get; }

    public DifferentialComparisonMode ComparisonMode { get; }

    public byte[]? StandardInput { get; }

    public static DifferentialCase Exact(params string[] arguments)
    {
        return new DifferentialCase(DifferentialComparisonMode.Exact, arguments);
    }

    public static DifferentialCase ExactWithStandardInput(byte[] standardInput, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(standardInput);

        return new DifferentialCase(DifferentialComparisonMode.Exact, standardInput, arguments);
    }

    public static DifferentialCase Normalized(DifferentialComparisonMode comparisonMode, params string[] arguments)
    {
        if (comparisonMode == DifferentialComparisonMode.Exact)
        {
            throw new ArgumentException("Use Exact for exact differential cases.", nameof(comparisonMode));
        }

        return new DifferentialCase(comparisonMode, arguments);
    }
}
