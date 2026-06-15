namespace Scout;

internal readonly record struct RegexNfaByteClassCacheKey(byte Start, byte End, bool Utf8, int Next);
