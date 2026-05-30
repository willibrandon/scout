using System;

namespace Scout;

/// <summary>
/// Provides helper methods for allocation-free match iteration.
/// </summary>
public static class MatchIterator
{
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
}
