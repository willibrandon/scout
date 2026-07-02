
namespace Scout.IO.Globbing;

/// <summary>
/// Configures and builds byte-oriented glob patterns.
/// </summary>
public sealed class GlobBuilder
{
    private readonly byte[] pattern;
    private bool literalSeparator;
    private bool backslashEscapes;
    private bool asciiCaseInsensitive;
    private byte[] pathSeparators;
    private bool matchBaseName;
    private bool emptyAlternates;
    private bool allowUnclosedClass;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobBuilder" /> class.
    /// </summary>
    /// <param name="pattern">The glob pattern bytes.</param>
    public GlobBuilder(byte[] pattern)
        : this(pattern, GlobOptions.Unix)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobBuilder" /> class.
    /// </summary>
    /// <param name="pattern">The glob pattern bytes.</param>
    /// <param name="options">The initial glob options.</param>
    public GlobBuilder(byte[] pattern, GlobOptions options)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);

        this.pattern = pattern.AsSpan().ToArray();
        literalSeparator = options.LiteralSeparator;
        backslashEscapes = options.BackslashEscapes;
        asciiCaseInsensitive = options.AsciiCaseInsensitive;
        pathSeparators = options.PathSeparators.ToArray();
        matchBaseName = options.MatchBaseName;
        emptyAlternates = options.EmptyAlternates;
        allowUnclosedClass = options.AllowUnclosedClass;
    }

    /// <summary>
    /// Sets whether wildcards are prevented from matching path separators.
    /// </summary>
    /// <param name="enabled">Whether separators must be matched literally.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithLiteralSeparator(bool enabled)
    {
        literalSeparator = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether backslash escapes glob metacharacters.
    /// </summary>
    /// <param name="enabled">Whether backslash escapes are enabled.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithBackslashEscapes(bool enabled)
    {
        backslashEscapes = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether ASCII byte case is ignored.
    /// </summary>
    /// <param name="enabled">Whether ASCII case-insensitive matching is enabled.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithAsciiCaseInsensitive(bool enabled)
    {
        asciiCaseInsensitive = enabled;
        return this;
    }

    /// <summary>
    /// Sets the path separator bytes.
    /// </summary>
    /// <param name="separators">The bytes treated as path separators.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithPathSeparators(byte[] separators)
    {
        ArgumentNullException.ThrowIfNull(separators);

        pathSeparators = separators.AsSpan().ToArray();
        return this;
    }

    /// <summary>
    /// Sets whether separator-free patterns can match only the final path component.
    /// </summary>
    /// <param name="enabled">Whether basename matching is enabled.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithMatchBaseName(bool enabled)
    {
        matchBaseName = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether empty brace alternatives are accepted.
    /// </summary>
    /// <param name="enabled">Whether empty alternatives are accepted.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithEmptyAlternates(bool enabled)
    {
        emptyAlternates = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether unclosed character classes are treated as literals.
    /// </summary>
    /// <param name="enabled">Whether unclosed classes are accepted as literals.</param>
    /// <returns>This builder.</returns>
    public GlobBuilder WithAllowUnclosedClass(bool enabled)
    {
        allowUnclosedClass = enabled;
        return this;
    }

    /// <summary>
    /// Builds the configured glob.
    /// </summary>
    /// <returns>A parsed glob.</returns>
    public Glob Build()
    {
        return Glob.Parse(
            pattern,
            new GlobOptions(
                literalSeparator,
                backslashEscapes,
                asciiCaseInsensitive,
                pathSeparators,
                matchBaseName,
                emptyAlternates,
                allowUnclosedClass));
    }
}
