using System.Text;

namespace Scout;

/// <summary>
/// Emits ripgrep-style diagnostic messages as raw stderr bytes.
/// </summary>
public sealed class DiagnosticMessenger
{
    private static readonly DiagnosticState SharedState = new();

    private readonly RawByteWriter error;
    private readonly DiagnosticState state;
    private readonly object gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticMessenger" /> class.
    /// </summary>
    /// <param name="error">The raw stderr writer.</param>
    public DiagnosticMessenger(RawByteWriter error)
        : this(error, SharedState)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticMessenger" /> class.
    /// </summary>
    /// <param name="error">The raw stderr writer.</param>
    /// <param name="state">The diagnostic state to update.</param>
    public DiagnosticMessenger(RawByteWriter error, DiagnosticState state)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(state);
        this.error = error;
        this.state = state;
    }

    /// <summary>
    /// Gets a value indicating whether an error message has been emitted.
    /// </summary>
    public bool HasErrored => state.HasErrored;

    /// <summary>
    /// Gets the process-wide diagnostic state.
    /// </summary>
    public static DiagnosticState ProcessState => SharedState;

    /// <summary>
    /// Writes an informational diagnostic message.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void Message(string message)
    {
        WriteLine(message);
    }

    /// <summary>
    /// Writes an error diagnostic message and sets the error flag.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void ErrorMessage(string message)
    {
        state.SetErrored();
        WriteLine(message);
    }

    /// <summary>
    /// Writes an error diagnostic from a Scout error cause chain.
    /// </summary>
    /// <param name="error">The error to write.</param>
    public void ErrorMessage(ScoutError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ErrorMessage(error.FormatAlternate());
    }

    /// <summary>
    /// Marks the process as having encountered a non-fatal error without writing output.
    /// </summary>
    public void MarkErrored()
    {
        state.SetErrored();
    }

    /// <summary>
    /// Clears the error flag without writing output.
    /// </summary>
    public void ResetErrored()
    {
        state.Reset();
    }

    private void WriteLine(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] bytes = Encoding.UTF8.GetBytes(message);
        lock (gate)
        {
            error.WriteLine(bytes);
            error.Flush();
        }
    }
}
