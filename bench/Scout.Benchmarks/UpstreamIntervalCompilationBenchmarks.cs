using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using Scout.Text.Regex;

namespace Scout;

/// <summary>
/// Measures general class compilation with unordered scalar intervals and case folding.
/// </summary>
[MemoryDiagnoser]
public class UpstreamIntervalCompilationBenchmarks
{
    private string pattern = string.Empty;

    /// <summary>
    /// Gets or sets the number of inserted scalar intervals.
    /// </summary>
    [Params(16, 256, 4096)]
    public int Intervals { get; set; }

    /// <summary>
    /// Gets or sets the engine mode used by the shared compiler.
    /// </summary>
    [Params(ByteRegexEngineMode.Optimized, ByteRegexEngineMode.General, ByteRegexEngineMode.AutomataOnly)]
    public ByteRegexEngineMode EngineMode { get; set; }

    /// <summary>
    /// Builds unordered intervals and verifies compiled output before measurement.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var expression = new StringBuilder("(?i)[");
        for (int index = Intervals; index > 0; index--)
        {
            expression.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\x{{{index * 7:X}}}-\\x{{{(index * 7) + 3:X}}}");
        }

        pattern = expression.Append(']').ToString();
        ByteRegex regex = Compile();
        if (!regex.IsMatch([7]) || regex.IsMatch([0]))
        {
            throw new InvalidOperationException("Interval compilation output changed.");
        }

        VerifyReference(regex);
    }

    private void VerifyReference(ByteRegex regex)
    {
        string oracle = Environment.GetEnvironmentVariable("SCOUT_TEST_RIPGREP_PATH")
            ?? throw new InvalidOperationException("Set SCOUT_TEST_RIPGREP_PATH to the release executable.");
        var text = new StringBuilder();
        for (int scalar = 0; scalar <= (Intervals * 7) + 4; scalar++)
        {
            if (scalar != '\n')
            {
                text.Append((char)scalar);
            }
        }

        byte[] input = Encoding.UTF8.GetBytes(text.ToString());
        using var expected = new MemoryStream();
        MemoryStream state = expected;
        regex.ForEachMatch(input, ref state, static (ReadOnlySpan<byte> bytes, ByteRegexMatch match, ref MemoryStream output) =>
        {
            output.Write(bytes.Slice(match.Start, match.Length));
            output.WriteByte((byte)'\n');
            return true;
        });
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, input);
            var start = new ProcessStartInfo(oracle)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "--no-config", "--text", "--multiline", "--only-matching", "--", pattern, path })
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Reference process did not start.");
            using var actual = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(actual);
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !expected.ToArray().AsSpan().SequenceEqual(actual.ToArray()))
            {
                throw new InvalidOperationException("Reference interval output differs: " + error);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Compiles the same general class expression on every invocation.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [Benchmark]
    public ByteRegex Compile() => ByteRegex.Compile(pattern, new ByteRegexOptions { EngineMode = EngineMode });
}
