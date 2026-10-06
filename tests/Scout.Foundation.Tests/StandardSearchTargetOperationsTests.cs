using System.Buffers;

namespace Scout;

/// <summary>
/// Verifies recursive standard-search target planning.
/// </summary>
[TestClass]
public sealed class StandardSearchTargetOperationsTests
{
    /// <summary>
    /// Verifies pooled raw reads discover an unknown file length only after the file is opened.
    /// </summary>
    [TestMethod]
    public void PooledRawFileReadDiscoversUnknownLength()
    {
        byte[] expected = "unknown file length"u8.ToArray();
        string path = Path.GetTempFileName();
        byte[]? rented = null;
        try
        {
            File.WriteAllBytes(path, expected);
            var lowArgs = new CliLowArgs();

            Assert.IsTrue(StandardSearchTargetOperations.CanUsePooledRawFileRead(
                knownLength: null,
                lowArgs,
                autoMmapEligible: false));
            Assert.IsTrue(StandardSearchTargetOperations.TryReadPooledRawFile(
                path,
                knownLength: null,
                CliEncodingMode.None,
                out rented,
                out int byteLength));
            Assert.AreSequenceEqual(expected.AsSpan(), rented.AsSpan(0, byteLength));
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
