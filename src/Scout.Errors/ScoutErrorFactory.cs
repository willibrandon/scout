
namespace Scout;

/// <summary>
/// Creates <see cref="ScoutError" /> instances from common runtime errors.
/// </summary>
public static class ScoutErrorFactory
{
    /// <summary>
    /// Converts an exception and its inner exceptions into a Scout error chain.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <returns>A Scout error preserving the exception message chain.</returns>
    public static ScoutError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        ScoutError? cause = exception.InnerException is null
            ? null
            : FromException(exception.InnerException);
        return new ScoutError(exception.Message, cause);
    }
}
