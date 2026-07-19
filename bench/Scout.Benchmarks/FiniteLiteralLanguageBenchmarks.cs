using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Compares flat and nested spellings of the same finite literal language.
/// </summary>
[MemoryDiagnoser]
public class FiniteLiteralLanguageBenchmarks
{
    private byte[] _haystack = [];
    private ByteRegex _flat = null!;
    private ByteRegex _nested = null!;

    /// <summary>
    /// Gets or sets whether the generated haystack contains matches.
    /// </summary>
    [Params(false, true)]
    public bool Matching { get; set; }

    /// <summary>
    /// Builds deterministic input and compiles equivalent flat and nested expressions.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        const string flatMatch = "(?:Generated|PaladinRecord|PaladinValue)";
        const string nestedMatch = "(?:Generated|Paladin(?:Record|Value))";
        const string flatNoMatch = "(?:Absent|MissingTwo|MissingThree)";
        const string nestedNoMatch = "(?:Absent|Missing(?:Two|Three))";

        var builder = new StringBuilder(capacity: 4_000_000);
        for (int index = 0; index < 32_768; index++)
        {
            builder.Append("internal sealed class GeneratedRecord");
            builder.Append(index);
            builder.Append(" { private readonly int _state; }\n");
            builder.Append("alpha bravo charlie delta echo foxtrot\n");
        }

        if (Matching)
        {
            builder.Append("internal sealed class PaladinRecord\n");
            builder.Append("internal sealed class PaladinValue\n");
        }

        _haystack = Encoding.UTF8.GetBytes(builder.ToString());
        var options = new ByteRegexOptions { EngineMode = ByteRegexEngineMode.General };
        _flat = ByteRegex.Compile(Matching ? flatMatch : flatNoMatch, options);
        _nested = ByteRegex.Compile(Matching ? nestedMatch : nestedNoMatch, options);
    }

    /// <summary>
    /// Counts matches with the flat finite-language spelling.
    /// </summary>
    /// <returns>The non-overlapping match count.</returns>
    [Benchmark(Baseline = true)]
    public long FlatCount()
    {
        return _flat.Count(_haystack);
    }

    /// <summary>
    /// Counts matches with the equivalent nested finite-language spelling.
    /// </summary>
    /// <returns>The non-overlapping match count.</returns>
    [Benchmark]
    public long NestedCount()
    {
        return _nested.Count(_haystack);
    }
}
