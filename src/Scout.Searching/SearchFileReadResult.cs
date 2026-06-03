
namespace Scout;

/// <summary>
/// Contains file bytes and the strategy used to read them.
/// </summary>
public sealed class SearchFileReadResult
{
    private readonly byte[] _bytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFileReadResult" /> class.
    /// </summary>
    /// <param name="bytes">The decoded bytes to search.</param>
    /// <param name="kind">The strategy used to read the file.</param>
    public SearchFileReadResult(byte[] bytes, SearchFileReadKind kind)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        _bytes = bytes;
        Kind = kind;
    }

    /// <summary>
    /// Gets the decoded bytes to search.
    /// </summary>
    /// <returns>The decoded bytes to search.</returns>
    public byte[] GetBytes()
    {
        return _bytes;
    }

    /// <summary>
    /// Gets the strategy used to read the file.
    /// </summary>
    public SearchFileReadKind Kind { get; }
}
