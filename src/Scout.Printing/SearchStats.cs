
namespace Scout;

internal struct SearchStats
{
    public ulong ElapsedNanoseconds { get; private set; }

    public ulong Searches { get; private set; }

    public ulong SearchesWithMatch { get; private set; }

    public ulong BytesSearched { get; private set; }

    public ulong BytesPrinted { get; private set; }

    public ulong MatchedLines { get; private set; }

    public ulong Matches { get; private set; }

    public void AddElapsed(TimeSpan elapsed)
    {
        ElapsedNanoseconds += (ulong)(elapsed.Ticks * 100);
    }

    public void AddSearch()
    {
        Searches++;
    }

    public void AddSearchWithMatch()
    {
        SearchesWithMatch++;
    }

    public void AddBytesSearched(ulong bytes)
    {
        BytesSearched += bytes;
    }

    public void AddBytesPrinted(ulong bytes)
    {
        BytesPrinted += bytes;
    }

    public void AddMatchedLine()
    {
        MatchedLines++;
    }

    public void AddMatchedLines(ulong lines)
    {
        MatchedLines += lines;
    }

    public void AddMatches(ulong matches)
    {
        Matches += matches;
    }

    public void Add(SearchStats other)
    {
        ElapsedNanoseconds += other.ElapsedNanoseconds;
        Searches += other.Searches;
        SearchesWithMatch += other.SearchesWithMatch;
        BytesSearched += other.BytesSearched;
        BytesPrinted += other.BytesPrinted;
        MatchedLines += other.MatchedLines;
        Matches += other.Matches;
    }
}
