
namespace Scout;

/// <summary>
/// Parses byte-oriented regex syntax into a Scout syntax tree.
/// </summary>
public static class RegexSyntaxParser
{
    /// <summary>
    /// Parses a regex pattern.
    /// </summary>
    /// <param name="pattern">The pattern bytes to parse.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="FormatException">Thrown when the pattern is not syntactically valid.</exception>
    public static RegexSyntaxTree Parse(ReadOnlySpan<byte> pattern)
    {
        return new RegexSyntaxParseState(pattern.ToArray()).Parse();
    }
}
