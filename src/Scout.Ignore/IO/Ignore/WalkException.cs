namespace Scout.IO.Ignore;

/// <summary>
/// Reports a traversal failure with its byte-preserving path and depth relative to the root.
/// </summary>
public sealed class WalkException : IOException
{
    /// <summary>
    /// Initializes an exception without path context.
    /// </summary>
    public WalkException()
    {
        Path = OsString.Empty;
    }

    /// <summary>
    /// Initializes an exception with a message and no path context.
    /// </summary>
    /// <param name="message">The error message.</param>
    public WalkException(string? message)
        : base(message)
    {
        Path = OsString.Empty;
    }

    /// <summary>
    /// Initializes an exception retaining its message and underlying cause.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public WalkException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Path = OsString.Empty;
    }

    /// <summary>
    /// Initializes a traversal exception retaining the underlying filesystem error.
    /// </summary>
    /// <param name="path">The path associated with the failure.</param>
    /// <param name="depth">The entry depth relative to the walk root.</param>
    /// <param name="innerException">The underlying filesystem exception.</param>
    public WalkException(OsString path, int depth, Exception innerException)
        : base(innerException?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        Path = path;
        Depth = depth;
    }

    /// <summary>
    /// Gets the operating-system path associated with the failure.
    /// </summary>
    public OsString Path { get; }

    /// <summary>
    /// Gets the entry depth relative to the walk root.
    /// </summary>
    public int Depth { get; }
}
