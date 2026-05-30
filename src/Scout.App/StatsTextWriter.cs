using System;
using System.Globalization;
using System.Text;

namespace Scout;

internal static class StatsTextWriter
{
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static void Write(RawByteWriter output, SearchStats stats, TimeSpan totalElapsed)
    {
        output.Write("\n"u8);
        WriteNumberLine(output, stats.Matches, " matches");
        WriteNumberLine(output, stats.MatchedLines, " matched lines");
        WriteNumberLine(output, stats.SearchesWithMatch, " files contained matches");
        WriteNumberLine(output, stats.Searches, " files searched");
        WriteNumberLine(output, stats.BytesPrinted, " bytes printed");
        WriteNumberLine(output, stats.BytesSearched, " bytes searched");
        WriteDurationLine(output, stats.ElapsedNanoseconds, " seconds spent searching");
        WriteDurationLine(output, (ulong)(totalElapsed.Ticks * 100), " seconds total");
    }

    private static void WriteNumberLine(RawByteWriter output, ulong value, string suffix)
    {
        WriteAscii(output, value.ToString(CultureInfo.InvariantCulture));
        WriteAscii(output, suffix);
        output.Write("\n"u8);
    }

    private static void WriteDurationLine(RawByteWriter output, ulong nanoseconds, string suffix)
    {
        double seconds = nanoseconds / 1_000_000_000.0;
        WriteAscii(output, seconds.ToString("0.000000", CultureInfo.InvariantCulture));
        WriteAscii(output, suffix);
        output.Write("\n"u8);
    }

    private static void WriteAscii(RawByteWriter output, string value)
    {
        output.Write(Ascii.GetBytes(value));
    }
}
