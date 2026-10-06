namespace Scout;

/// <summary>
/// Verifies raw standard-stream platform error classification.
/// </summary>
[TestClass]
public sealed class RawStandardStreamsTests
{
    /// <summary>
    /// Verifies Windows pipe closure errors terminate standard-input reads normally.
    /// </summary>
    /// <param name="error">The Win32 pipe closure error.</param>
    [TestMethod]
    [DataRow(109)]
    [DataRow(232)]
    public void WindowsStandardInputPipeClosureIsEndOfFile(int error)
    {
        Assert.IsTrue(RawStandardStreams.IsWindowsStandardInputEndOfFile(error));
    }

    /// <summary>
    /// Verifies unrelated Windows read errors remain observable failures.
    /// </summary>
    /// <param name="error">The Win32 error to classify.</param>
    [TestMethod]
    [DataRow(32)]
    [DataRow(5)]
    [DataRow(233)]
    public void OtherWindowsStandardInputErrorsAreNotEndOfFile(int error)
    {
        Assert.IsFalse(RawStandardStreams.IsWindowsStandardInputEndOfFile(error));
    }

    /// <summary>
    /// Verifies Windows downstream pipe closure retains its independent output classification.
    /// </summary>
    /// <param name="error">The Win32 pipe closure error.</param>
    [TestMethod]
    [DataRow(109)]
    [DataRow(232)]
    public void WindowsOutputPipeClosureRetainsOutputClassification(int error)
    {
        var exception = new IOException(
            "pipe closed",
            RawStandardStreams.GetIoErrorHResult(error));

        Assert.AreEqual(OperatingSystem.IsWindows(), RawStandardStreams.IsBrokenPipe(exception));
    }
}
