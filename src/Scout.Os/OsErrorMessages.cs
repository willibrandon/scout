using System;
using System.ComponentModel;

namespace Scout;

/// <summary>
/// Formats operating-system diagnostics that ripgrep renders differently by platform.
/// </summary>
public static class OsErrorMessages
{
    /// <summary>
    /// Gets the platform-specific missing-file diagnostic.
    /// </summary>
    public static string NoSuchFileOrDirectory =>
        OperatingSystem.IsWindows()
            ? "The system cannot find the file specified. (os error 2)"
            : "No such file or directory (os error 2)";

    /// <summary>
    /// Gets the platform-specific permission-denied diagnostic.
    /// </summary>
    public static string PermissionDenied =>
        OperatingSystem.IsWindows()
            ? "Access is denied. (os error 5)"
            : "Permission denied (os error 13)";

    /// <summary>
    /// Gets the platform-specific diagnostic for a directory used where a file is expected.
    /// </summary>
    public static string DirectoryAsFile =>
        OperatingSystem.IsWindows()
            ? PermissionDenied
            : "Is a directory (os error 21)";

    /// <summary>
    /// Formats a Win32 exception with ripgrep's numeric <c>os error</c> suffix.
    /// </summary>
    /// <param name="exception">The exception to format.</param>
    /// <returns>The formatted diagnostic message.</returns>
    public static string FormatWin32Exception(Win32Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return OperatingSystem.IsWindows() && exception.NativeErrorCode > 0
            ? $"{exception.Message} (os error {exception.NativeErrorCode})"
            : exception.Message;
    }
}
