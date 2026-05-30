using System;

namespace Scout;

/// <summary>
/// Represents an error that occurs while parsing a glob pattern.
/// </summary>
public sealed class GlobParseException : Exception
{
    private readonly byte[] globPattern;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobParseException" /> class.
    /// </summary>
    public GlobParseException()
    {
        ErrorKind = GlobParseErrorKind.Unknown;
        globPattern = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobParseException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GlobParseException(string message)
        : base(message)
    {
        ErrorKind = GlobParseErrorKind.Unknown;
        globPattern = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobParseException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public GlobParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorKind = GlobParseErrorKind.Unknown;
        globPattern = [];
    }

    internal GlobParseException(GlobParseErrorKind errorKind, ReadOnlySpan<byte> pattern, byte? rangeStart = null, byte? rangeEnd = null)
        : base(CreateMessage(errorKind))
    {
        ErrorKind = errorKind;
        globPattern = pattern.ToArray();
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }

    /// <summary>
    /// Gets the glob parse error kind.
    /// </summary>
    public GlobParseErrorKind ErrorKind { get; }

    /// <summary>
    /// Gets the pattern bytes that failed to parse.
    /// </summary>
    public ReadOnlyMemory<byte> GlobPattern => globPattern;

    /// <summary>
    /// Gets the invalid range start byte, when the error is an invalid range.
    /// </summary>
    public byte? RangeStart { get; }

    /// <summary>
    /// Gets the invalid range end byte, when the error is an invalid range.
    /// </summary>
    public byte? RangeEnd { get; }

    private static string CreateMessage(GlobParseErrorKind errorKind)
    {
        return errorKind switch
        {
            GlobParseErrorKind.UnclosedClass => "unclosed character class; missing ']'",
            GlobParseErrorKind.InvalidRange => "invalid character range",
            GlobParseErrorKind.UnopenedAlternates => "unopened alternate group; missing '{' (maybe escape '}' with '[}]'?)",
            GlobParseErrorKind.UnclosedAlternates => "unclosed alternate group; missing '}' (maybe escape '{' with '[{]'?)",
            GlobParseErrorKind.DanglingEscape => "dangling '\\'",
            _ => "glob parse error",
        };
    }
}
