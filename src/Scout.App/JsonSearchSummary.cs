using System;
using System.Diagnostics;

namespace Scout;

internal sealed class JsonSearchSummary
{
    private readonly long started = Stopwatch.GetTimestamp();
    private SearchStats stats;

    public void Add(SearchStats fileStats)
    {
        stats.Add(fileStats);
    }

    public void Add(JsonSearchSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        stats.Add(summary.stats);
    }

    public void WriteSummary(RawByteWriter output)
    {
        JsonByteWriter.WriteSummaryMessage(output, stats, Stopwatch.GetElapsedTime(started));
    }
}
