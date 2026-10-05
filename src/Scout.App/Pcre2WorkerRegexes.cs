namespace Scout;

/// <summary>
/// Owns the per-thread regexes while borrowing the first worker's existing regex.
/// </summary>
internal sealed class Pcre2WorkerRegexes : IDisposable
{
    private readonly Pcre2Regex _borrowed;
    private readonly ThreadLocal<Pcre2Regex> _workers;
    private bool _disposed;

    internal Pcre2WorkerRegexes(Pcre2Regex borrowed, byte[] pattern, Pcre2CompileOptions options)
    {
        _borrowed = borrowed;
        int borrowedClaimed = 0;
        _workers = new ThreadLocal<Pcre2Regex>(
            () => Interlocked.CompareExchange(ref borrowedClaimed, 1, 0) == 0
                ? borrowed
                : new Pcre2Regex(pattern, options),
            trackAllValues: true);
    }

    internal Pcre2Regex Value => _workers.Value!;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using (_workers)
        {
            foreach (Pcre2Regex regex in _workers.Values.Where(regex => !ReferenceEquals(regex, _borrowed)))
            {
                regex.Dispose();
            }
        }
    }
}
