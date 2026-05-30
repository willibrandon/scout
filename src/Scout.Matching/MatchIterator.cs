using System;

namespace Scout;

/// <summary>
/// Provides helper methods for allocation-free match iteration.
/// </summary>
public static class MatchIterator
{
    /// <summary>
    /// Identifies that no empty match start is currently suppressed.
    /// </summary>
    public const int NoSuppressedEmptyStart = -1;

    /// <summary>
    /// Advances an iterator past a match using ripgrep-compatible empty-match handling.
    /// </summary>
    /// <param name="match">The match that was just reported.</param>
    /// <param name="haystackLength">The haystack length in bytes.</param>
    /// <returns>The next start offset.</returns>
    public static int AdvanceAfter(MatcherMatch match, int haystackLength)
    {
        int next = match.End + (match.Length == 0 ? 1 : 0);
        return Math.Min(next, haystackLength + 1);
    }

    /// <summary>
    /// Advances an iterator past a match that was suppressed instead of reported.
    /// </summary>
    /// <param name="match">The suppressed match.</param>
    /// <param name="haystackLength">The haystack length in bytes.</param>
    /// <returns>The next start offset.</returns>
    public static int AdvanceAfterSuppressedEmpty(MatcherMatch match, int haystackLength)
    {
        return Math.Min(match.Start + 1, haystackLength + 1);
    }

    /// <summary>
    /// Advances an iterator past a reported match and updates empty-match suppression state.
    /// </summary>
    /// <param name="match">The match that was just reported.</param>
    /// <param name="haystackLength">The haystack length in bytes.</param>
    /// <param name="suppressedEmptyStart">The next empty-match start to suppress, or <see cref="NoSuppressedEmptyStart" />.</param>
    /// <returns>The next start offset.</returns>
    public static int AdvanceAfterReported(MatcherMatch match, int haystackLength, ref int suppressedEmptyStart)
    {
        if (match.Length == 0)
        {
            suppressedEmptyStart = NoSuppressedEmptyStart;
            return AdvanceAfter(match, haystackLength);
        }

        suppressedEmptyStart = Math.Min(match.End, haystackLength + 1);
        return suppressedEmptyStart;
    }

    /// <summary>
    /// Returns a value indicating whether an empty match should be suppressed after a preceding non-empty match.
    /// </summary>
    /// <param name="match">The candidate match.</param>
    /// <param name="suppressedEmptyStart">The empty-match start to suppress.</param>
    /// <returns><see langword="true" /> when the match is the suppressed empty match.</returns>
    public static bool IsSuppressedEmpty(MatcherMatch match, int suppressedEmptyStart)
    {
        return match.Length == 0 && match.Start == suppressedEmptyStart;
    }
}
