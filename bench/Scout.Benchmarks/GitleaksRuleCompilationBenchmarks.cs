using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures compilation of the representative Gitleaks assignment rule from issue #49.
/// </summary>
[MemoryDiagnoser]
public class GitleaksRuleCompilationBenchmarks()
{
    private readonly string _pattern = """(?i)[\w.-]{0,50}?(?:coinbase)(?:[ \t\w.-]{0,20})[\s'"]{0,3}(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)[\x60'"\s=]{0,5}([a-z0-9_-]{64})(?:[\x60'"\s;]|\\[nr]|$)""";

    /// <summary>
    /// Gets or sets the regex engine mode under measurement.
    /// </summary>
    [Params(
        ByteRegexEngineMode.Optimized,
        ByteRegexEngineMode.General,
        ByteRegexEngineMode.AutomataOnly)]
    public ByteRegexEngineMode EngineMode { get; set; }

    /// <summary>
    /// Compiles the representative assignment rule.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [Benchmark]
    public ByteRegex Compile()
    {
        return ByteRegex.Compile(
            _pattern,
            new ByteRegexOptions { EngineMode = EngineMode });
    }
}
