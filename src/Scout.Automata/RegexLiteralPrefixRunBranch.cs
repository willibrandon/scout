namespace Scout;

internal readonly struct RegexLiteralPrefixRunBranch(byte[] prefix, RegexLiteralPrefixRunKind runKind)
{
    public byte[] Prefix { get; } = prefix;

    public RegexLiteralPrefixRunKind RunKind { get; } = runKind;
}
