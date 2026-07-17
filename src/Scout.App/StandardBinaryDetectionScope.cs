namespace Scout;

/// <summary>
/// Selects which bytes determine binary handling for a standard search operation.
/// </summary>
internal enum StandardBinaryDetectionScope
{
    /// <summary>
    /// Detects binary input from the complete input buffer.
    /// </summary>
    WholeInput,

    /// <summary>
    /// Detects binary input from lines selected by the authoritative matcher.
    /// </summary>
    SelectedLines,
}
