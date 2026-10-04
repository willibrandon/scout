using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures general class compilation with unordered scalar intervals and case folding.
/// </summary>
[MemoryDiagnoser]
public class UpstreamIntervalCompilationBenchmarks
{
    private string pattern = string.Empty;

    /// <summary>
    /// Gets or sets the number of inserted scalar intervals.
    /// </summary>
    [Params(16, 256, 4096)]
    public int Intervals { get; set; }

    /// <summary>
    /// Gets or sets the engine mode used by the shared compiler.
    /// </summary>
    [Params(ByteRegexEngineMode.Optimized, ByteRegexEngineMode.General, ByteRegexEngineMode.AutomataOnly)]
    public ByteRegexEngineMode EngineMode { get; set; }

    /// <summary>
    /// Builds unordered intervals and verifies compiled output before measurement.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var expression = new StringBuilder("(?i)[");
        for (int index = Intervals; index > 0; index--)
        {
            expression.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\x{{{index * 7:X}}}-\\x{{{(index * 7) + 3:X}}}");
        }

        pattern = expression.Append(']').ToString();
        ByteRegex regex = Compile();
        if (!regex.IsMatch([7]) || regex.IsMatch([0]))
        {
            throw new InvalidOperationException("Interval compilation output changed.");
        }
    }

    /// <summary>
    /// Compiles the same general class expression on every invocation.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [Benchmark]
    public ByteRegex Compile() => ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = EngineMode });
}
