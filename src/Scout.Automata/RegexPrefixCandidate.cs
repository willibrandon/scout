namespace Scout;

internal readonly struct RegexPrefixCandidate
{
    public RegexPrefixCandidate(byte[] bytes, bool sealedPrefix, int preferredPrefixBytes)
    {
        Bytes = bytes;
        Sealed = sealedPrefix || bytes.Length >= preferredPrefixBytes;
    }

    public byte[] Bytes { get; }

    public bool Sealed { get; }
}
