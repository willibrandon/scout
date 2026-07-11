namespace Scout;

/// <summary>
/// Represents an active PikeVM thread and the candidate start that originated it.
/// </summary>
/// <param name="State">The active NFA state index.</param>
/// <param name="Position">The current byte position.</param>
/// <param name="Start">The originating candidate start.</param>
internal readonly record struct PikeVmThread(int State, int Position, int Start);
