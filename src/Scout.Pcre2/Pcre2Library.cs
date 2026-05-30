using System;

namespace Scout;

/// <summary>
/// Describes the PCRE2 runtime linked into the current Scout build.
/// </summary>
public static class Pcre2Library
{
    /// <summary>
    /// Gets a value indicating whether this build has a linked PCRE2 runtime.
    /// </summary>
    public const bool IsAvailable = false;

    /// <summary>
    /// Gets the ripgrep-compatible error message used when PCRE2 is requested but not linked.
    /// </summary>
    public const string UnavailableErrorMessage = "PCRE2 is not available in this build of ripgrep";

    /// <summary>
    /// Gets the <c>--pcre2-version</c> output for a build without PCRE2.
    /// </summary>
    public static ReadOnlySpan<byte> UnavailableVersionOutput =>
        "PCRE2 is not available in this build of ripgrep.\n"u8;
}
