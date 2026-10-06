using System.IO.MemoryMappedFiles;

namespace Scout;

/// <summary>
/// Exercises sequential advice against real mappings and native failure responses.
/// </summary>
[TestClass]
public sealed unsafe class NativeMemoryAdviceTests
{
    /// <summary>
    /// Verifies advice failures log at debug level while both mapping consumers retain valid data.
    /// </summary>
    [TestMethod]
    public void AdviceFailureRemainsNonfatalForMappedReaders()
    {
        string root = Directory.CreateTempSubdirectory("scout-madvise-").FullName;
        try
        {
            string path = Path.Join(root, "file");
            File.WriteAllBytes(path, "needle\n"u8.ToArray());
            using var output = new MemoryStream();
            var diagnostics = new DiagnosticMessenger(new RawByteWriter(output), new DiagnosticState());
            var logger = new DiagnosticLogger(diagnostics, CliLoggingMode.Debug);
            int calls = 0;
            int Fail(nint address, nuint length)
            {
                Assert.AreNotEqual(0, address);
                Assert.IsGreaterThanOrEqualTo((nuint)7, length);
                calls++;
                return 22;
            }

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(path, out MemoryMappedSearchFile? mapped, logger, Fail));
            using (mapped)
            {
                Assert.IsNotNull(mapped);
                Assert.IsTrue(mapped.TryMapView(0, 7));
                Assert.IsTrue(mapped.Bytes.SequenceEqual("needle\n"u8));
            }

            SearchFileReadResult read = SearchFileReader.Read(path, SearchEncodingKind.Auto,
                SearchMmapMode.AlwaysTryMmap, true, 7, logger, Fail);
            Assert.AreSequenceEqual("needle\n"u8.ToArray(), read.GetBytes());
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Assert.IsGreaterThan(0, calls);
                Assert.Contains("madvise failed", System.Text.Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
                Assert.IsFalse(diagnostics.HasErrored);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies advice preserves bytes and a failed hint does not affect a subsequent valid mapping.
    /// </summary>
    [TestMethod]
    public void SequentialAdvicePreservesLiveMapping()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, 4096);
        using MemoryMappedViewAccessor view = mapping.CreateViewAccessor();
        view.Write(0, (byte)42);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            Assert.IsTrue(NativeMemoryAdvice.TrySequential(pointer, (nuint)view.SafeMemoryMappedViewHandle.ByteLength, out int error));
            Assert.AreEqual(0, error);
            Assert.AreEqual(42, pointer[0]);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                Assert.IsFalse(NativeMemoryAdvice.TrySequential((byte*)1, 4096, out error));
                Assert.AreNotEqual(0, error);
                Assert.AreEqual(42, pointer[0]);
                Assert.IsTrue(NativeMemoryAdvice.TrySequential(pointer, 4096, out error));
                Assert.AreEqual(0, error);
            }

            Assert.IsTrue(NativeMemoryAdvice.TrySequential(null, 0, out error));
            Assert.AreEqual(0, error);
        }
        finally
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }
}
