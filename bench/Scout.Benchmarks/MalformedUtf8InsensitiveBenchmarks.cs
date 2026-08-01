using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures the opt-in option's overhead for a pattern that cannot observe U+FFFD.
/// </summary>
[MemoryDiagnoser]
public class MalformedUtf8InsensitiveBenchmarks
{
    private byte[] _haystack = [];
    private ByteRegex _defaultRegex = null!;
    private ByteRegex _optInRegex = null!;

    /// <summary>
    /// Gets or sets the synthetic input size in bytes.
    /// </summary>
    [Params(1 * 1024 * 1024, 8 * 1024 * 1024)]
    public int Size { get; set; }

    /// <summary>
    /// Creates deterministic input and reusable replacement-insensitive regexes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _haystack = GC.AllocateUninitializedArray<byte>(Size);
        uint state = 0x56;
        for (int index = 0; index < _haystack.Length; index++)
        {
            state = unchecked((state * 1_664_525) + 1_013_904_223);
            _haystack[index] = (byte)('a' + ((state >> 24) % 26));
        }

        _haystack[^1] = 0xFF;
        const string pattern = "a-literal-that-does-not-occur";
        _defaultRegex = ByteRegex.Compile(pattern);
        _optInRegex = ByteRegex.Compile(
            pattern,
            new ByteRegexOptions { MatchInvalidUtf8 = true });
    }

    /// <summary>
    /// Searches with the ripgrep-compatible default.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    public long Default()
    {
        return _defaultRegex.Count(_haystack);
    }

    /// <summary>
    /// Searches with malformed matching enabled for an insensitive pattern.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public long OptInInsensitive()
    {
        return _optInRegex.Count(_haystack);
    }
}
