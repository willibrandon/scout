using System.Collections.Generic;

namespace Scout;

internal readonly struct LargeFileSegmentSearchResult
{
    public LargeFileSegmentSearchResult(
        bool matched,
        ulong matchedLines,
        nint segmentAddress,
        int segmentLength,
        long segmentStartOffset,
        long segmentLineNumber,
        List<LargeFileSegmentMatch>? matches)
    {
        Matched = matched;
        MatchedLines = matchedLines;
        SegmentAddress = segmentAddress;
        SegmentLength = segmentLength;
        SegmentStartOffset = segmentStartOffset;
        SegmentLineNumber = segmentLineNumber;
        Matches = matches;
    }

    public bool Matched { get; }

    public ulong MatchedLines { get; }

    public nint SegmentAddress { get; }

    public int SegmentLength { get; }

    public long SegmentStartOffset { get; }

    public long SegmentLineNumber { get; }

    public List<LargeFileSegmentMatch>? Matches { get; }
}
