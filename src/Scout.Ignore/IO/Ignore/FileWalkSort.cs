namespace Scout.IO.Ignore;

/// <summary>
/// Specifies deterministic sorting for entries yielded by <see cref="FileWalker" />.
/// </summary>
public enum FileWalkSort
{
    /// <summary>
    /// Preserve filesystem enumeration order.
    /// </summary>
    None,

    /// <summary>
    /// Sort entries in each directory by full path.
    /// </summary>
    FullPath,

    /// <summary>
    /// Sort entries in each directory by file name.
    /// </summary>
    FileName,
}
