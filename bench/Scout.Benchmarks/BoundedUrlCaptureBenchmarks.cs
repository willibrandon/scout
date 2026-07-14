using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures issue #34's bounded URL match and capture search.
/// </summary>
[MemoryDiagnoser]
public class BoundedUrlCaptureBenchmarks()
{
    private const string Input = "DATABASE_URL=\"postgresql://app_user:picket-db-password-123@db.internal.local:5432/appdb?sslmode=require\"";
    private const string Pattern = """(?i)\b((?:postgres(?:ql)?|mysql|mariadb|sqlserver|mongodb(?:\+srv)?|redis)://[^:/?#@\s'"\x60;]{1,128}:[^@\s'"\x60;]{8,256}@[^\s'"\x60<>;]{3,512})(?:[\x60'"\s;]|\\[nr]|$)""";

    private byte[] _haystack = [];
    private ByteRegex _regex = null!;

    /// <summary>
    /// Gets or sets the regex engine mode under measurement.
    /// </summary>
    [Params(
        ByteRegexEngineMode.Optimized,
        ByteRegexEngineMode.General,
        ByteRegexEngineMode.AutomataOnly)]
    public ByteRegexEngineMode EngineMode { get; set; }

    /// <summary>
    /// Compiles and warms the issue pattern against its deterministic input.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _haystack = Encoding.UTF8.GetBytes(Input);
        _regex = ByteRegex.Compile(Pattern, new ByteRegexOptions { EngineMode = EngineMode });

        _ = _regex.Find(_haystack);
        _ = _regex.FindCaptures(_haystack);
    }

    /// <summary>
    /// Searches for the first match without capture replay.
    /// </summary>
    /// <returns>The first bounded URL match.</returns>
    [Benchmark(Baseline = true)]
    public ByteRegexMatch? Find()
    {
        return _regex.Find(_haystack);
    }

    /// <summary>
    /// Searches for the first match and its capture groups.
    /// </summary>
    /// <returns>The first bounded URL capture set.</returns>
    [Benchmark]
    public ByteRegexCaptures? FindCaptures()
    {
        return _regex.FindCaptures(_haystack);
    }
}
