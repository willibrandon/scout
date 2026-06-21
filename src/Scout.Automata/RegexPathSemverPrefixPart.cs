namespace Scout;

internal readonly record struct RegexPathSemverPrefixPart(byte[]? Literal, bool[]? ByteMatches);
