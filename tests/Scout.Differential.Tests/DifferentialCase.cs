using System;

namespace Scout;

internal sealed class DifferentialCase
{
    public DifferentialCase(DifferentialComparisonMode comparisonMode, params string[] arguments)
    {
        Arguments = arguments;
        ComparisonMode = comparisonMode;
    }

    public string[] Arguments { get; }

    public DifferentialComparisonMode ComparisonMode { get; }

    public static DifferentialCase Exact(params string[] arguments)
    {
        return new DifferentialCase(DifferentialComparisonMode.Exact, arguments);
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
