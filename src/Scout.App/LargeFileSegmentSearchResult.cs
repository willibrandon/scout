namespace Scout;

internal readonly struct LargeFileSegmentSearchResult
{
    public LargeFileSegmentSearchResult(bool matched, ulong matchedLines, string? outputPath)
    {
        Matched = matched;
        MatchedLines = matchedLines;
        OutputPath = outputPath;
    }

    public bool Matched { get; }

    public ulong MatchedLines { get; }

    public string? OutputPath { get; }
}
