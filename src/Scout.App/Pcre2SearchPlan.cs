namespace Scout;

/// <summary>
/// Owns the compiled PCRE2 matcher and prepared pattern data for one search operation.
/// </summary>
/// <param name="patterns">The prepared ordered pattern set.</param>
/// <param name="pattern">The combined PCRE2 pattern.</param>
/// <param name="compileOptions">The options used to compile the matcher.</param>
internal sealed class Pcre2SearchPlan(
    List<byte[]> patterns,
    byte[] pattern,
    Pcre2CompileOptions compileOptions) : IDisposable
{
    private readonly List<byte[]> _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    private readonly byte[] _pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    private readonly Pcre2CompileOptions _compileOptions = compileOptions;
    private readonly Pcre2Regex _regex = new(pattern, compileOptions);

    /// <summary>
    /// Gets the prepared ordered pattern set.
    /// </summary>
    internal List<byte[]> Patterns => _patterns;

    /// <summary>
    /// Gets the combined PCRE2 pattern.
    /// </summary>
    internal byte[] Pattern => _pattern;

    /// <summary>
    /// Gets the options used to compile the matcher.
    /// </summary>
    internal Pcre2CompileOptions CompileOptions => _compileOptions;

    /// <summary>
    /// Gets the compiled PCRE2 matcher retained for search dispatch.
    /// </summary>
    internal Pcre2Regex Regex => _regex;

    /// <inheritdoc />
    public void Dispose()
    {
        _regex.Dispose();
    }
}
