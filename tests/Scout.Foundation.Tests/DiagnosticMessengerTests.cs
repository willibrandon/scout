using System.Text;

namespace Scout;

/// <summary>
/// Verifies raw diagnostic message output.
/// </summary>
public sealed class DiagnosticMessengerTests
{
    /// <summary>
    /// Verifies informational diagnostics do not set the error flag.
    /// </summary>
    [Fact]
    public void MessageWritesUtf8WithoutErroredFlag()
    {
        using MemoryStream stream = new();
        var messenger = new DiagnosticMessenger(new RawByteWriter(stream), new DiagnosticState());

        messenger.Message("debug: snowman \u2603");

        Assert.False(messenger.HasErrored);
        Assert.Equal(Encoding.UTF8.GetBytes("debug: snowman \u2603\n"), stream.ToArray());
    }

    /// <summary>
    /// Verifies error diagnostics set the error flag.
    /// </summary>
    [Fact]
    public void ErrorMessageWritesChainAndSetsErroredFlag()
    {
        using MemoryStream stream = new();
        var state = new DiagnosticState();
        var messenger = new DiagnosticMessenger(new RawByteWriter(stream), state);
        ScoutError error = new ScoutError("leaf").WithContext("root");

        messenger.ErrorMessage(error);

        Assert.True(messenger.HasErrored);
        Assert.True(state.HasErrored);
        Assert.Equal("root: leaf\n"u8.ToArray(), stream.ToArray());
    }

    /// <summary>
    /// Verifies the error flag can be reset between command invocations.
    /// </summary>
    [Fact]
    public void ResetErroredClearsFlag()
    {
        using MemoryStream stream = new();
        var messenger = new DiagnosticMessenger(new RawByteWriter(stream), new DiagnosticState());

        messenger.ErrorMessage("error");
        messenger.ResetErrored();

        Assert.False(messenger.HasErrored);
    }

    /// <summary>
    /// Verifies messengers can share process-wide error state.
    /// </summary>
    [Fact]
    public void MessengersCanShareGlobalState()
    {
        using MemoryStream first = new();
        using MemoryStream second = new();
        DiagnosticMessenger.ProcessState.Reset();
        var firstMessenger = new DiagnosticMessenger(new RawByteWriter(first));
        var secondMessenger = new DiagnosticMessenger(new RawByteWriter(second));

        firstMessenger.ErrorMessage("error");

        Assert.True(secondMessenger.HasErrored);
        DiagnosticMessenger.ProcessState.Reset();
    }
}
