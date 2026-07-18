using System.Collections.Concurrent;

namespace Scout;

/// <summary>
/// Reuses bounded match buffers across parallel large-file segments.
/// </summary>
/// <param name="maximumRetainedCount">The maximum number of match buffers retained by the pool.</param>
internal sealed class LargeFileSegmentMatchPool(int maximumRetainedCount)
{
    private readonly ConcurrentBag<List<LargeFileSegmentMatch>> _items = [];
    private readonly int _maximumRetainedCount =
        maximumRetainedCount > 0
            ? maximumRetainedCount
            : throw new ArgumentOutOfRangeException(nameof(maximumRetainedCount));
    private int _retainedCount;

    /// <summary>
    /// Rents an empty match buffer.
    /// </summary>
    /// <returns>An empty match buffer owned by the caller.</returns>
    public List<LargeFileSegmentMatch> Rent()
    {
        if (_items.TryTake(out List<LargeFileSegmentMatch>? matches))
        {
            Interlocked.Decrement(ref _retainedCount);
            return matches;
        }

        return [];
    }

    /// <summary>
    /// Clears and returns a match buffer when the pool has available capacity.
    /// </summary>
    /// <param name="matches">The match buffer to return.</param>
    public void Return(List<LargeFileSegmentMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        matches.Clear();

        int retainedCount = Interlocked.Increment(ref _retainedCount);
        if (retainedCount <= _maximumRetainedCount)
        {
            _items.Add(matches);
            return;
        }

        Interlocked.Decrement(ref _retainedCount);
    }
}
