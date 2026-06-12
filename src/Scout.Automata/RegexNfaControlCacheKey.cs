namespace Scout;

internal readonly record struct RegexNfaControlCacheKey(RegexNfaStateKind Kind, int Next, int Alternative);
