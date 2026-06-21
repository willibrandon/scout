namespace Scout;

internal readonly record struct RegexNfaSparseTransition(byte Start, byte End, int Next);
