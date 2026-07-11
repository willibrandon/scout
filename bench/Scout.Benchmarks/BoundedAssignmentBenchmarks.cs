using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures issue #30's repeated bounded-assignment no-match search.
/// </summary>
[MemoryDiagnoser]
public class BoundedAssignmentBenchmarks()
{
    private const string Pattern = """(?i)[\w.-]{0,50}?(?:bitbucket)(?:[ \t\w.-]{0,20})[\s'"]{0,3}(?:=|>|:{1,3}=|\|\||:|=>|\?=|,)[\x60'"\s=]{0,5}([a-z0-9]{32})(?:[\x60'"\s;]|\\[nr]|$)""";
    private const string CandidateLine = "bitbucket repository setting without a credential\n";

    private byte[] _haystack = [];
    private ByteRegex _regex = null!;

    /// <summary>
    /// Gets or sets the number of repeated required-literal candidates in the input.
    /// </summary>
    [Params(1, 100, 800)]
    public int CandidateCount { get; set; }

    /// <summary>
    /// Gets or sets the regex engine mode under measurement.
    /// </summary>
    [Params(ByteRegexEngineMode.Optimized, ByteRegexEngineMode.AutomataOnly)]
    public ByteRegexEngineMode EngineMode { get; set; }

    /// <summary>
    /// Compiles the issue pattern and builds its deterministic no-match input.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder(CandidateLine.Length * CandidateCount);
        for (int index = 0; index < CandidateCount; index++)
        {
            builder.Append(CandidateLine);
        }

        _haystack = Encoding.UTF8.GetBytes(builder.ToString());
        _regex = ByteRegex.Compile(Pattern, new ByteRegexOptions { EngineMode = EngineMode });
    }

    /// <summary>
    /// Searches for the first match.
    /// </summary>
    /// <returns>The first match, which is absent for this input.</returns>
    [Benchmark]
    public ByteRegexMatch? Find()
    {
        return _regex.Find(_haystack);
    }

    /// <summary>
    /// Counts all matches.
    /// </summary>
    /// <returns>Zero for this input.</returns>
    [Benchmark]
    public long Count()
    {
        return _regex.Count(_haystack);
    }

    /// <summary>
    /// Searches for the first match and its capture groups.
    /// </summary>
    /// <returns>The first capture set, which is absent for this input.</returns>
    [Benchmark]
    public ByteRegexCaptures? FindCaptures()
    {
        return _regex.FindCaptures(_haystack);
    }
}
