namespace Scout;

internal delegate RegexMatch? RegexUnguardedFindDelegate(ReadOnlySpan<byte> haystack, int startAt);
