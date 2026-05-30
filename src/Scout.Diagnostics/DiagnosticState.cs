using System.Threading;

namespace Scout;

/// <summary>
/// Tracks process-wide diagnostic state.
/// </summary>
public sealed class DiagnosticState
{
    private int hasErrored;

    /// <summary>
    /// Gets a value indicating whether an error diagnostic has been emitted.
    /// </summary>
    public bool HasErrored => Volatile.Read(ref hasErrored) != 0;

    /// <summary>
    /// Marks the process as having emitted an error diagnostic.
    /// </summary>
    public void SetErrored()
    {
        Volatile.Write(ref hasErrored, 1);
    }

    /// <summary>
    /// Clears the error state.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref hasErrored, 0);
    }
}
