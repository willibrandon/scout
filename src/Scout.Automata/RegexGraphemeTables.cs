namespace Scout;

internal static class RegexGraphemeTables
{
    private static readonly byte[] ClassByScalar = CreateClassByScalar();
    private static readonly byte[] ExtendedPictographicByScalar = CreateExtendedPictographicByScalar();

    public static RegexGraphemeClass Classify(int scalar)
    {
        return (uint)scalar < ClassByScalar.Length
            ? (RegexGraphemeClass)ClassByScalar[scalar]
            : RegexGraphemeClass.Other;
    }

    public static bool IsExtendedPictographic(int scalar)
    {
        return (uint)scalar < ExtendedPictographicByScalar.Length &&
            ExtendedPictographicByScalar[scalar] != 0;
    }

    private static byte[] CreateClassByScalar()
    {
        byte[] table = new byte[0x110000];
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakCr, RegexGraphemeClass.Cr);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakLf, RegexGraphemeClass.Lf);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakControl, RegexGraphemeClass.Control);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakExtend, RegexGraphemeClass.Extend);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakL, RegexGraphemeClass.L);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakV, RegexGraphemeClass.V);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakLv, RegexGraphemeClass.Lv);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakLvt, RegexGraphemeClass.Lvt);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakT, RegexGraphemeClass.T);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakPrepend, RegexGraphemeClass.Prepend);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakRegionalIndicator, RegexGraphemeClass.RegionalIndicator);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakSpacingMark, RegexGraphemeClass.SpacingMark);
        Fill(table, RegexUnicodePropertyKind.GraphemeClusterBreakZwj, RegexGraphemeClass.Zwj);
        return table;
    }

    private static byte[] CreateExtendedPictographicByScalar()
    {
        byte[] table = new byte[0x110000];
        ReadOnlySpan<int> ranges = RegexUnicodeTables.GetBooleanPropertyRanges(
            RegexUnicodePropertyKind.ExtendedPictographic);
        for (int index = 0; index < ranges.Length; index += 2)
        {
            Array.Fill(table, (byte)1, ranges[index], ranges[index + 1] - ranges[index] + 1);
        }

        return table;
    }

    private static void Fill(byte[] table, RegexUnicodePropertyKind kind, RegexGraphemeClass graphemeClass)
    {
        ReadOnlySpan<int> ranges = RegexUnicodeTables.GetBreakPropertyRanges(kind);
        for (int index = 0; index < ranges.Length; index += 2)
        {
            Array.Fill(table, (byte)graphemeClass, ranges[index], ranges[index + 1] - ranges[index] + 1);
        }
    }
}
