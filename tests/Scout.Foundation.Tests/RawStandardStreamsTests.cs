namespace Scout;

/// <summary>
/// Verifies raw standard-stream platform error classification.
/// </summary>
public sealed class RawStandardStreamsTests
{
    /// <summary>
    /// Verifies Windows pipe closure errors terminate standard-input reads normally.
    /// </summary>
    /// <param name="error">The Win32 pipe closure error.</param>
    [Theory]
    [InlineData(109)]
    [InlineData(232)]
    public void WindowsStandardInputPipeClosureIsEndOfFile(int error)
    {
        Assert.True(RawStandardStreams.IsWindowsStandardInputEndOfFile(error));
    }

    /// <summary>
    /// Verifies unrelated Windows read errors remain observable failures.
    /// </summary>
    /// <param name="error">The Win32 error to classify.</param>
    [Theory]
    [InlineData(32)]
    [InlineData(5)]
    [InlineData(233)]
    public void OtherWindowsStandardInputErrorsAreNotEndOfFile(int error)
    {
        Assert.False(RawStandardStreams.IsWindowsStandardInputEndOfFile(error));
    }

    /// <summary>
    /// Verifies Windows downstream pipe closure retains its independent output classification.
    /// </summary>
    /// <param name="error">The Win32 pipe closure error.</param>
    [Theory]
    [InlineData(109)]
    [InlineData(232)]
    public void WindowsOutputPipeClosureRetainsOutputClassification(int error)
    {
        var exception = new IOException(
            "pipe closed",
            RawStandardStreams.GetIoErrorHResult(error));

        Assert.Equal(OperatingSystem.IsWindows(), RawStandardStreams.IsBrokenPipe(exception));
    }
}
