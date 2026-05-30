namespace Scout;

/// <summary>
/// Identifies how a search file was read.
/// </summary>
public enum SearchFileReadKind
{
    /// <summary>
    /// The file was read through a buffered stream.
    /// </summary>
    Buffered,

    /// <summary>
    /// The file was read through a memory-mapped view.
    /// </summary>
    MemoryMapped,
}
