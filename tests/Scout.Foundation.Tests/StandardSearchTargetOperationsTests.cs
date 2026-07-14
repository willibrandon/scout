using System.Buffers;

namespace Scout;

/// <summary>
/// Verifies recursive standard-search target planning.
/// </summary>
public sealed class StandardSearchTargetOperationsTests
{
    /// <summary>
    /// Verifies regex candidate prechecks do not require directory metadata to have been resolved eagerly.
    /// </summary>
    [Fact]
    public void RegexCandidatePrecheckAcceptsLazyDirectoryEntryMetadata()
    {
        byte[][] pattern = ["foo.*bar"u8.ToArray()];
        RegexSearchPlan? regexPlan = LiteralLineSearcher.CreateRegexSearchPlan(
            pattern,
            asciiCaseInsensitive: false,
            compileAutomata: true);
        var entry = new DirEntry(
            "lazy-entry",
            depth: 1,
            attributes: default,
            isDirectory: false,
            isSymbolicLink: false,
            isStdin: false,
            length: null,
            identity: default,
            deferMetadata: true);

        Assert.Null(entry.KnownLength);
        Assert.True(StandardSearchTargetOperations.CanUseDirectoryEntryRegexCandidatePrecheck(
            entry,
            pattern,
            new CliLowArgs(),
            regexPlan,
            out RegexCandidateLineAccelerator? accelerator));
        Assert.NotNull(accelerator);
    }

    /// <summary>
    /// Verifies pooled raw reads discover an unknown file length only after the file is opened.
    /// </summary>
    [Fact]
    public void PooledRawFileReadDiscoversUnknownLength()
    {
        byte[] expected = "unknown file length"u8.ToArray();
        string path = Path.GetTempFileName();
        byte[]? rented = null;
        try
        {
            File.WriteAllBytes(path, expected);
            var lowArgs = new CliLowArgs();

            Assert.True(StandardSearchTargetOperations.CanUsePooledRawFileRead(
                knownLength: null,
                lowArgs,
                autoMmapEligible: false));
            Assert.True(StandardSearchTargetOperations.TryReadPooledRawFile(
                path,
                knownLength: null,
                CliEncodingMode.None,
                out rented,
                out int byteLength));
            Assert.Equal(expected, rented.AsSpan(0, byteLength));
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            File.Delete(path);
        }
    }
}
