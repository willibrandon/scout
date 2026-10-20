namespace Scout;

/// <summary>
/// Decodes the named special escapes that regex-syntax accepts.
/// </summary>
/// <remarks>
/// The parser and every fast path that re-reads character class source share this table, so a
/// new escape only has to be added here. The set matches <c>ast::SpecialLiteralKind</c> in
/// regex-syntax: bell, form feed, tab, line feed, carriage return, and vertical tab.
/// </remarks>
internal static class RegexSpecialEscape
{
    /// <summary>
    /// Decodes the byte that follows a backslash when it names a special escape.
    /// </summary>
    /// <param name="escaped">The byte after the backslash.</param>
    /// <param name="value">The decoded byte when the escape is special.</param>
    /// <returns><see langword="true" /> when <paramref name="escaped" /> is <c>a</c>, <c>f</c>, <c>t</c>, <c>n</c>, <c>r</c>, or <c>v</c>.</returns>
    public static bool TryDecode(byte escaped, out byte value)
    {
        switch (escaped)
        {
            case (byte)'a':
                value = 0x07;
                return true;
            case (byte)'f':
                value = 0x0C;
                return true;
            case (byte)'t':
                value = (byte)'\t';
                return true;
            case (byte)'n':
                value = (byte)'\n';
                return true;
            case (byte)'r':
                value = (byte)'\r';
                return true;
            case (byte)'v':
                value = 0x0B;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
