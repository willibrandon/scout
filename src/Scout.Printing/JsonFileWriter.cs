using System.Diagnostics;

namespace Scout;

internal sealed class JsonFileWriter
{
    private readonly RawByteWriter output;
    private readonly byte[] path;
    private readonly bool quiet;
    private readonly int binaryOffset;
    private readonly long started = Stopwatch.GetTimestamp();
    private bool beginPrinted;
    private ulong bytesPrinted;
    private SearchStats stats;

    public JsonFileWriter(RawByteWriter output, byte[] path, bool quiet, int binaryOffset)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(path);
        this.output = output;
        this.path = path;
        this.quiet = quiet;
        this.binaryOffset = binaryOffset;
    }

    public void WriteMatchLine(
        long lineNumber,
        long absoluteOffset,
        ReadOnlySpan<byte> line,
        IReadOnlyList<JsonMatchSpan> matches,
        ulong matchedLineCount = 1)
    {
        stats.AddMatchedLines(matchedLineCount);
        stats.AddMatches((ulong)matches.Count);
        if (quiet)
        {
            return;
        }

        WriteBeginMessage();
        using MemoryStream buffer = new();
        var writer = new RawByteWriter(buffer);
        JsonByteWriter.WriteMatchMessage(writer, path, line, lineNumber, absoluteOffset, matches);
        WriteBufferedMessage(buffer, writer);
    }

    public void WriteContextLine(long lineNumber, long absoluteOffset, ReadOnlySpan<byte> line, IReadOnlyList<JsonMatchSpan> matches)
    {
        if (quiet)
        {
            return;
        }

        WriteBeginMessage();
        using MemoryStream buffer = new();
        var writer = new RawByteWriter(buffer);
        JsonByteWriter.WriteContextMessage(writer, path, line, lineNumber, absoluteOffset, matches);
        WriteBufferedMessage(buffer, writer);
    }

    public void Finish(ulong bytesSearched, JsonSearchSummary summary)
    {
        stats.AddElapsed(Stopwatch.GetElapsedTime(started));
        stats.AddSearch();
        if (stats.MatchedLines > 0)
        {
            stats.AddSearchWithMatch();
        }

        stats.AddBytesSearched(bytesSearched);
        stats.AddBytesPrinted(bytesPrinted);
        summary.Add(stats);
        if (!quiet && beginPrinted)
        {
            JsonByteWriter.WriteEndMessage(output, path, binaryOffset, stats);
        }
    }

    private void WriteBeginMessage()
    {
        if (beginPrinted)
        {
            return;
        }

        using MemoryStream buffer = new();
        var writer = new RawByteWriter(buffer);
        JsonByteWriter.WriteBeginMessage(writer, path);
        WriteBufferedMessage(buffer, writer);
        beginPrinted = true;
    }

    private void WriteBufferedMessage(MemoryStream buffer, RawByteWriter writer)
    {
        writer.Flush();
        byte[] bytes = buffer.ToArray();
        output.Write(bytes);
        bytesPrinted += (ulong)bytes.Length;
    }
}
