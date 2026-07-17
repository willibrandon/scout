namespace Scout;

/// <summary>
/// Reports parsed regex syntax that explicitly consumes an excluded record terminator.
/// </summary>
internal sealed class RegexLineTerminatorException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegexLineTerminatorException" /> class.
    /// </summary>
    public RegexLineTerminatorException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexLineTerminatorException" /> class.
    /// </summary>
    /// <param name="message">The diagnostic that describes the excluded terminator.</param>
    public RegexLineTerminatorException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexLineTerminatorException" /> class.
    /// </summary>
    /// <param name="message">The diagnostic that describes the excluded terminator.</param>
    /// <param name="innerException">The exception that caused the current failure.</param>
    public RegexLineTerminatorException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
