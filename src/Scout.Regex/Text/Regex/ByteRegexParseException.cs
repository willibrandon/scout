namespace Scout.Text.Regex;

/// <summary>
/// The exception thrown when a byte regex pattern cannot be parsed.
/// </summary>
public sealed class ByteRegexParseException : Exception
{
    private const string OffsetMarker = " at byte offset ";

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexParseException" /> class.
    /// </summary>
    public ByteRegexParseException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexParseException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ByteRegexParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexParseException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ByteRegexParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteRegexParseException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="offset">The byte offset where parsing failed, or <see langword="null" /> when unknown.</param>
    /// <param name="innerException">The inner exception.</param>
    public ByteRegexParseException(string message, int? offset, Exception? innerException)
        : base(message, innerException)
    {
        Offset = offset;
    }

    /// <summary>
    /// Gets the byte offset where parsing failed, or <see langword="null" /> when unknown.
    /// </summary>
    public int? Offset { get; }

    internal static ByteRegexParseException FromFormatException(FormatException exception)
    {
        string message = exception.Message;
        int? offset = TryParseOffset(message);
        return new ByteRegexParseException(message, offset, exception);
    }

    private static int? TryParseOffset(string message)
    {
        int markerIndex = message.LastIndexOf(OffsetMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        string offsetText = message[(markerIndex + OffsetMarker.Length)..];
        return int.TryParse(offsetText, out int offset) ? offset : null;
    }
}
