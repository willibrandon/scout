using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures issue #61's generic API-key rule with its trailing end assertion.
/// </summary>
[MemoryDiagnoser]
public class GitleaksEndAssertionBenchmarks
{
    private const int InputLength = 4 * 1024 * 1024;
    private const int CandidateSpacing = 64;
    private const string Pattern = "(?i)[0-9A-Z_a-z.-]{0,50}?(?:access|auth|(?-i:[Aa]pi|API)|credential|creds|key|passw(?:or)?d|secret|token)(?:[ \\t0-9A-Z_a-z.-]{0,20})[\\t\\n\\f\\r '\"]{0,3}(?:=|>|:{1,3}=|\\|\\||:|=>|\\?=|,)[\\x60'\"\\t\\n\\f\\r =]{0,5}([0-9A-Z_a-z.=-]{10,150}|[a-z0-9][a-z0-9+/]{11,}={0,3})(?:[\\x60'\"\\t\\n\\f\\r ;]|\\\\[nr]|$)";

    private byte[] _input = [];
    private ByteRegex _contextualRegex = null!;
    private ByteRegex _pikeVmRegex = null!;

    /// <summary>
    /// Gets or sets whether every candidate record ends in malformed UTF-8.
    /// </summary>
    [Params(false, true)]
    public bool Invalid { get; set; }

    /// <summary>
    /// Compiles the exact rule, creates its deterministic corpus, and warms the selected runner.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _contextualRegex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions
            {
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
            });
        _pikeVmRegex = ByteRegex.Compile(
            Pattern,
            new ByteRegexOptions
            {
                EngineMode = ByteRegexEngineMode.General,
                MatchInvalidUtf8 = true,
                DfaSizeLimit = 0,
            });
        _input = GC.AllocateUninitializedArray<byte>(InputLength);
        _input.AsSpan().Fill((byte)'x');
        ReadOnlySpan<byte> candidate = "api_key candidate "u8;
        for (int offset = 0; offset + CandidateSpacing <= _input.Length; offset += CandidateSpacing)
        {
            candidate.CopyTo(_input.AsSpan(offset));
            if (Invalid)
            {
                _input[offset + CandidateSpacing - 1] = 0xff;
            }
        }

        if (_contextualRegex.FindCaptures(_input) is not null ||
            _pikeVmRegex.FindCaptures(_input) is not null)
        {
            throw new InvalidOperationException("The no-match benchmark corpus unexpectedly matched.");
        }
    }

    /// <summary>
    /// Searches the complete no-match corpus with the authoritative PikeVM.
    /// </summary>
    /// <returns>No captures for the deterministic input.</returns>
    [Benchmark(Baseline = true)]
    public ByteRegexCaptures? PikeVmFindCaptures()
    {
        return _pikeVmRegex.FindCaptures(_input);
    }

    /// <summary>
    /// Searches the complete no-match corpus with bounded contextual lazy determinization.
    /// </summary>
    /// <returns>No captures for the deterministic input.</returns>
    [Benchmark]
    public ByteRegexCaptures? ContextualLazyDfaFindCaptures()
    {
        return _contextualRegex.FindCaptures(_input);
    }
}
