namespace Scout;

/// <summary>
/// Owns storage allocated only when a record spans input buffers or mapped views.
/// </summary>
internal sealed class PendingLineBuffer : IDisposable
{
    private MemoryStream? _stream;

    /// <summary>
    /// Gets whether no record is currently being assembled.
    /// </summary>
    internal bool IsEmpty => _stream is null;

    /// <summary>
    /// Gets the number of buffered bytes.
    /// </summary>
    internal long Length => _stream?.Length ?? 0;

    /// <summary>
    /// Appends a fragment, allocating storage on the first fragment.
    /// </summary>
    /// <param name="fragment">The record fragment.</param>
    internal void Write(ReadOnlySpan<byte> fragment)
    {
        _stream ??= new MemoryStream();
        _stream.Write(fragment);
    }

    /// <summary>
    /// Copies the completed record.
    /// </summary>
    /// <returns>The buffered bytes.</returns>
    internal byte[] ToArray() => _stream?.ToArray() ?? [];

    /// <summary>
    /// Gets the backing storage for immediate, allocation-free processing.
    /// </summary>
    /// <returns>The backing array; only <see cref="Length" /> bytes contain record data.</returns>
    internal byte[] GetBuffer() => _stream?.GetBuffer() ?? [];

    /// <summary>
    /// Releases the current record's storage.
    /// </summary>
    internal void Clear()
    {
        _stream?.Dispose();
        _stream = null;
    }

    /// <inheritdoc />
    public void Dispose() => Clear();
}
