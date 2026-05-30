using System;
using System.Collections.Generic;
using System.Linq;

namespace Scout;

/// <summary>
/// Represents a ripgrep-style error with an ordered cause chain.
/// </summary>
public sealed class ScoutError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScoutError" /> class.
    /// </summary>
    /// <param name="message">The user-facing error message.</param>
    /// <param name="cause">The underlying cause, if any.</param>
    public ScoutError(string message, ScoutError? cause = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        Message = message;
        Cause = cause;
    }

    /// <summary>
    /// Gets the user-facing error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the underlying cause.
    /// </summary>
    public ScoutError? Cause { get; }

    /// <summary>
    /// Creates a new error that adds context above this error.
    /// </summary>
    /// <param name="message">The context message.</param>
    /// <returns>A new error with this instance as its cause.</returns>
    public ScoutError WithContext(string message)
    {
        return new ScoutError(message, this);
    }

    /// <summary>
    /// Enumerates this error followed by each cause.
    /// </summary>
    /// <returns>The ordered error chain.</returns>
    public IEnumerable<ScoutError> Chain()
    {
        for (ScoutError? error = this; error is not null; error = error.Cause)
        {
            yield return error;
        }
    }

    /// <summary>
    /// Formats only this error's display message.
    /// </summary>
    /// <returns>The default display form.</returns>
    public string FormatDefault()
    {
        return Message;
    }

    /// <summary>
    /// Formats this error and its cause chain as ripgrep prints top-level anyhow errors.
    /// </summary>
    /// <returns>The alternate display form.</returns>
    public string FormatAlternate()
    {
        return string.Join(": ", Chain().Select(static error => error.Message));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormatDefault();
    }
}
