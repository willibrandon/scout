using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures issue #32's large bounded Unicode-class compilation and cold search.
/// </summary>
[MemoryDiagnoser]
public class LargeBoundedUnicodeClassBenchmarks
{
    private const int CandidateCount = 5000;
    private const string CandidateLine = "x[\\w-]{50,1000}\n";

    private byte[] _haystack = [];
    private string _asciiPattern = string.Empty;
    private string _unicodePattern = string.Empty;

    /// <summary>
    /// Gets or sets the maximum repetition bound under measurement.
    /// </summary>
    [Params(50, 100, 200, 1000)]
    public int Maximum { get; set; }

    /// <summary>
    /// Builds the deterministic patterns and no-match input.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _asciiPattern = $"x[A-Za-z0-9_-]{{50,{Maximum}}}";
        _unicodePattern = $"x[\\w-]{{50,{Maximum}}}";

        var builder = new StringBuilder(CandidateLine.Length * CandidateCount);
        for (int index = 0; index < CandidateCount; index++)
        {
            builder.Append(CandidateLine);
        }

        _haystack = Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Compiles the ASCII-equivalent bounded class.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [Benchmark(Baseline = true)]
    public ByteRegex CompileAscii()
    {
        return ByteRegex.Compile(
            _asciiPattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
    }

    /// <summary>
    /// Compiles the Unicode bounded class.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [Benchmark]
    public ByteRegex CompileUnicode()
    {
        return ByteRegex.Compile(
            _unicodePattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
    }

    /// <summary>
    /// Compiles the ASCII-equivalent class and performs its first no-match search.
    /// </summary>
    /// <returns><see langword="false" /> for the deterministic input.</returns>
    [Benchmark]
    public bool CompileAndRejectAscii()
    {
        var regex = ByteRegex.Compile(
            _asciiPattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        return regex.IsMatch(_haystack);
    }

    /// <summary>
    /// Compiles the Unicode class and performs its first no-match search.
    /// </summary>
    /// <returns><see langword="false" /> for the deterministic input.</returns>
    [Benchmark]
    public bool CompileAndRejectUnicode()
    {
        var regex = ByteRegex.Compile(
            _unicodePattern,
            new ByteRegexOptions { EngineMode = ByteRegexEngineMode.AutomataOnly });
        return regex.IsMatch(_haystack);
    }
}
