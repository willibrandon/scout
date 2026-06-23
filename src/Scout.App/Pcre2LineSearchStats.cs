namespace Scout;

internal struct Pcre2LineSearchStats
{
    public ulong BytesSearched { get; private set; }

    public ulong MatchedLines { get; private set; }

    public ulong Matches { get; private set; }

    public void SetBytesSearched(ulong bytes)
    {
        BytesSearched = bytes;
    }

    public void AddMatchedLine()
    {
        MatchedLines++;
    }

    public void AddMatches(ulong matches)
    {
        Matches += matches;
    }

    public readonly SearchStats ToSearchStats(ulong bytesPrinted, TimeSpan elapsed)
    {
        var stats = new SearchStats();
        stats.AddElapsed(elapsed);
        stats.AddSearch();
        stats.AddBytesPrinted(bytesPrinted);
        stats.AddBytesSearched(BytesSearched);
        stats.AddMatchedLines(MatchedLines);
        stats.AddMatches(Matches);
        if (MatchedLines > 0)
        {
            stats.AddSearchWithMatch();
        }

        return stats;
    }
}
