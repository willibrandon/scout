using System;
using BenchmarkDotNet.Attributes;

namespace Scout;

/// <summary>
/// Benchmarks the literal line-search hot path used by the Scout application.
/// </summary>
[MemoryDiagnoser]
public sealed class LiteralSearchBenchmarks
{
    private byte[] haystack = [];
    private byte[] needle = [];

    /// <summary>
    /// Creates deterministic benchmark data before each benchmark run.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        byte[] line = "alpha beta gamma needle delta epsilon\n"u8.ToArray();
        haystack = new byte[line.Length * 32_768];
        for (int offset = 0; offset < haystack.Length; offset += line.Length)
        {
            line.CopyTo(haystack.AsSpan(offset));
        }

        needle = "needle"u8.ToArray();
    }

    /// <summary>
    /// Searches a large in-memory buffer for a literal pattern.
    /// </summary>
    /// <returns><see langword="true" /> when at least one matching line exists.</returns>
    [Benchmark]
    public bool HasLiteralMatch()
    {
        return LiteralLineSearcher.HasMatch(haystack, needle);
    }

    /// <summary>
    /// Counts all literal matches in a large in-memory buffer.
    /// </summary>
    /// <returns>The number of non-overlapping literal matches.</returns>
    [Benchmark]
    public long CountLiteralMatches()
    {
        return LiteralLineSearcher.CountMatches(haystack, needle);
    }
}
