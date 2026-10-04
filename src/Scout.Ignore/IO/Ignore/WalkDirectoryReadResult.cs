namespace Scout.IO.Ignore;

/// <summary>
/// Retains successful directory entries alongside contextual enumeration errors.
/// </summary>
/// <param name="entries">The successfully read entries.</param>
/// <param name="errors">The contextual errors encountered while reading.</param>
internal readonly struct WalkDirectoryReadResult(WalkPath[] entries, WalkException[] errors)
{
    /// <summary>
    /// Gets the successfully read entries.
    /// </summary>
    public WalkPath[] Entries { get; } = entries;

    /// <summary>
    /// Gets the directory opening or enumeration errors.
    /// </summary>
    public WalkException[] Errors { get; } = errors;
}
