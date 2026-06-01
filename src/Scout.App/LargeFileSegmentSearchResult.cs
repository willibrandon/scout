namespace Scout;

internal readonly struct LargeFileSegmentSearchResult
{
    public LargeFileSegmentSearchResult(bool matched, ulong matchedLines, ReadOnlyMemory<byte> outputBytes)
    {
        Matched = matched;
        MatchedLines = matchedLines;
        OutputBytes = outputBytes;
    }

    public bool Matched { get; }

    public ulong MatchedLines { get; }

    public ReadOnlyMemory<byte> OutputBytes { get; }
}
