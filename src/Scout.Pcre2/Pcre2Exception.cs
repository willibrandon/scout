using System.Globalization;

namespace Scout;

/// <summary>
/// Represents a PCRE2 compile or match failure.
/// </summary>
public sealed class Pcre2Exception : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Exception" /> class.
    /// </summary>
    public Pcre2Exception()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Exception" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public Pcre2Exception(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Exception" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public Pcre2Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Exception" /> class.
    /// </summary>
    /// <param name="errorCode">The PCRE2 error code.</param>
    /// <param name="offset">The pattern or subject offset associated with the error.</param>
    /// <param name="message">The PCRE2 error message.</param>
    public Pcre2Exception(int errorCode, nuint offset, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        Offset = offset;
    }

    /// <summary>
    /// Gets the PCRE2 error code.
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Gets the pattern or subject offset associated with the error.
    /// </summary>
    public nuint Offset { get; }

    internal static Pcre2Exception Create(int errorCode, nuint offset, string operation, string message)
    {
        return new Pcre2Exception(
            errorCode,
            offset,
            operation + " failed at offset " + offset.ToString(CultureInfo.InvariantCulture) + ": " + message);
    }
}
