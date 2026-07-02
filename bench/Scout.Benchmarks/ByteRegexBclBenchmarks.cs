using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

using BclRegex = System.Text.RegularExpressions.Regex;

namespace Scout;

/// <summary>
/// Compares Scout's byte regex facade with the BCL regex engines on text-valid input.
/// </summary>
[MemoryDiagnoser]
public partial class ByteRegexBclBenchmarks
{
    private const string GeneratedTypeDeclarationPattern = @"\b(?:struct|enum|union)\s+[A-Za-z_][A-Za-z0-9_]*";

    private byte[] haystackBytes = [];
    private string haystackText = string.Empty;
    private string typeDeclarationPattern = string.Empty;
    private ByteRegex scoutRegex = null!;
    private BclRegex bclRegex = null!;
    private BclRegex bclCompiledRegex = null!;
    private BclRegex bclNonBacktrackingRegex = null!;
    private BclRegex bclGeneratedRegex = null!;

    /// <summary>
    /// Builds deterministic UTF-8 and UTF-16 benchmark inputs and compiles reusable regexes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder(capacity: 1_250_000);
        for (int index = 0; index < 16_384; index++)
        {
            builder.Append("static int value_");
            builder.Append(index);
            builder.AppendLine(" = 42;");

            switch (index % 4)
            {
                case 0:
                    builder.Append("struct Type_");
                    break;
                case 1:
                    builder.Append("enum Type_");
                    break;
                case 2:
                    builder.Append("union Type_");
                    break;
                default:
                    builder.Append("class Type_");
                    break;
            }

            builder.Append(index);
            builder.AppendLine(" { int field; };");
        }

        haystackText = builder.ToString();
        haystackBytes = Encoding.UTF8.GetBytes(haystackText);
        typeDeclarationPattern = string.Concat(@"\b(?:", "struct|enum|union", @")\s+[A-Za-z_][A-Za-z0-9_]*");

        scoutRegex = ByteRegex.Compile(typeDeclarationPattern);
        bclRegex = new BclRegex(typeDeclarationPattern, RegexOptions.CultureInvariant);
        bclCompiledRegex = new BclRegex(typeDeclarationPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
        bclNonBacktrackingRegex = new BclRegex(typeDeclarationPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        bclGeneratedRegex = GeneratedTypeDeclarationRegex();
    }

    /// <summary>
    /// Counts all matching type declarations with Scout's byte-oriented regex.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    public long ScoutCount()
    {
        return scoutRegex.Count(haystackBytes);
    }

    /// <summary>
    /// Counts all matching type declarations with the BCL interpreted regex engine.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int BclInterpretedCount()
    {
        return bclRegex.Count(haystackText);
    }

    /// <summary>
    /// Counts all matching type declarations with the BCL compiled regex engine.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int BclCompiledCount()
    {
        return bclCompiledRegex.Count(haystackText);
    }

    /// <summary>
    /// Counts all matching type declarations with the BCL non-backtracking regex engine.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int BclNonBacktrackingCount()
    {
        return bclNonBacktrackingRegex.Count(haystackText);
    }

    /// <summary>
    /// Counts all matching type declarations with a BCL source-generated regex.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int BclGeneratedCount()
    {
        return bclGeneratedRegex.Count(haystackText);
    }

    [GeneratedRegex(GeneratedTypeDeclarationPattern, RegexOptions.CultureInvariant)]
    private static partial BclRegex GeneratedTypeDeclarationRegex();
}
