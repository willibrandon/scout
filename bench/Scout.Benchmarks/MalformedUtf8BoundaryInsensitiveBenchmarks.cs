using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures malformed matching for an ASCII expression preceded by Unicode boundary predicates.
/// </summary>
[MemoryDiagnoser]
public class MalformedUtf8BoundaryInsensitiveBenchmarks
{
    private const int HaystackLength = 16 * 1024 * 1024;
    private const int CandidateStride = 4 * 1024;
    private const string Pattern =
        @"(?u:\b|\B)(?-u:\b)ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?";

    private byte[] _haystack = [];
    private ByteRegex _defaultRegex = null!;
    private ByteRegex _optInRegex = null!;

    /// <summary>
    /// Creates deterministic malformed input with frequent plausible literal candidates.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _haystack = GC.AllocateUninitializedArray<byte>(HaystackLength);
        _haystack.AsSpan().Fill(0xFF);
        ReadOnlySpan<byte> candidate = "api_key = invalid-candidate "u8;
        for (int offset = 0; offset <= _haystack.Length - candidate.Length; offset += CandidateStride)
        {
            candidate.CopyTo(_haystack.AsSpan(offset));
        }

        _defaultRegex = ByteRegex.Compile(Pattern);
        _optInRegex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions { MatchInvalidUtf8 = true });

        if (_defaultRegex.Count(_haystack) != 0 || _optInRegex.Count(_haystack) != 0)
        {
            throw new InvalidOperationException("The synthetic no-match benchmark input unexpectedly matched.");
        }
    }

    /// <summary>
    /// Searches with ripgrep-compatible malformed UTF-8 handling.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    public long Default()
    {
        return _defaultRegex.Count(_haystack);
    }

    /// <summary>
    /// Searches with malformed bytes treated as replacement scalars where scalar consumption requires it.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public long OptInInsensitive()
    {
        return _optInRegex.Count(_haystack);
    }
}
