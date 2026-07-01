namespace Scout;

internal enum RegexSimpleSequenceByteMatcherKind
{
    Lookup,
    Literal,
    AsciiUppercase,
    AsciiLowercase,
    AsciiLetter,
    AsciiDigit,
    AsciiIdentifierStart,
    AsciiAlphanumeric,
    AsciiWord,
    RegexWhitespace,
    Any,
}
