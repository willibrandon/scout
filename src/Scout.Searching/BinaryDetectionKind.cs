namespace Scout;

/// <summary>
/// Describes how search input should treat binary NUL bytes.
/// </summary>
public enum BinaryDetectionKind
{
    /// <summary>
    /// Treat input as text and leave NUL bytes unchanged.
    /// </summary>
    None,

    /// <summary>
    /// Convert NUL bytes to line feeds for searching while retaining the original bytes for output.
    /// </summary>
    Convert,

    /// <summary>
    /// Stop searching when a NUL byte marks the input as binary.
    /// </summary>
    Quit,
}
