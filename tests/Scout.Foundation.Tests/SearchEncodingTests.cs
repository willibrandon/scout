namespace Scout;

/// <summary>
/// Verifies search input transcoding behavior.
/// </summary>
public sealed class SearchEncodingTests
{
    /// <summary>
    /// Verifies implemented WHATWG labels resolve to the expected search encoding kinds.
    /// </summary>
    /// <param name="label">The input encoding label.</param>
    /// <param name="expectedEncodingKind">The expected search encoding kind.</param>
    [Theory]
    [InlineData("utf-8", SearchEncodingKind.Utf8)]
    [InlineData("utf8", SearchEncodingKind.Utf8)]
    [InlineData("unicode20utf8", SearchEncodingKind.Utf8)]
    [InlineData("unicode11utf8", SearchEncodingKind.Utf8)]
    [InlineData("x-unicode20utf8", SearchEncodingKind.Utf8)]
    [InlineData("unicode-1-1-utf-8", SearchEncodingKind.Utf8)]
    [InlineData("utf-16", SearchEncodingKind.Utf16)]
    [InlineData("csunicode", SearchEncodingKind.Utf16Le)]
    [InlineData("iso-10646-ucs-2", SearchEncodingKind.Utf16Le)]
    [InlineData("ucs-2", SearchEncodingKind.Utf16Le)]
    [InlineData("unicode", SearchEncodingKind.Utf16Le)]
    [InlineData("unicodefeff", SearchEncodingKind.Utf16Le)]
    [InlineData("utf-16le", SearchEncodingKind.Utf16Le)]
    [InlineData("utf-16be", SearchEncodingKind.Utf16Be)]
    [InlineData("unicodefffe", SearchEncodingKind.Utf16Be)]
    [InlineData("korean", SearchEncodingKind.EucKr)]
    [InlineData("euc-kr", SearchEncodingKind.EucKr)]
    [InlineData("ksc5601", SearchEncodingKind.EucKr)]
    [InlineData("cseuckr", SearchEncodingKind.EucKr)]
    [InlineData("ksc_5601", SearchEncodingKind.EucKr)]
    [InlineData("iso-ir-149", SearchEncodingKind.EucKr)]
    [InlineData("windows-949", SearchEncodingKind.EucKr)]
    [InlineData("csksc56011987", SearchEncodingKind.EucKr)]
    [InlineData("ks_c_5601-1987", SearchEncodingKind.EucKr)]
    [InlineData("ks_c_5601-1989", SearchEncodingKind.EucKr)]
    [InlineData("cseucpkdfmtjapanese", SearchEncodingKind.EucJp)]
    [InlineData("euc-jp", SearchEncodingKind.EucJp)]
    [InlineData("x-euc-jp", SearchEncodingKind.EucJp)]
    [InlineData("big5", SearchEncodingKind.Big5)]
    [InlineData("big5-hkscs", SearchEncodingKind.Big5)]
    [InlineData("cn-big5", SearchEncodingKind.Big5)]
    [InlineData("csbig5", SearchEncodingKind.Big5)]
    [InlineData("x-x-big5", SearchEncodingKind.Big5)]
    [InlineData("gb18030", SearchEncodingKind.Gb18030)]
    [InlineData("chinese", SearchEncodingKind.Gbk)]
    [InlineData("csgb2312", SearchEncodingKind.Gbk)]
    [InlineData("csiso58gb231280", SearchEncodingKind.Gbk)]
    [InlineData("gb2312", SearchEncodingKind.Gbk)]
    [InlineData("gb_2312", SearchEncodingKind.Gbk)]
    [InlineData("gb_2312-80", SearchEncodingKind.Gbk)]
    [InlineData("gbk", SearchEncodingKind.Gbk)]
    [InlineData("iso-ir-58", SearchEncodingKind.Gbk)]
    [InlineData("x-gbk", SearchEncodingKind.Gbk)]
    [InlineData("csshiftjis", SearchEncodingKind.ShiftJis)]
    [InlineData("ms932", SearchEncodingKind.ShiftJis)]
    [InlineData("ms_kanji", SearchEncodingKind.ShiftJis)]
    [InlineData("shift-jis", SearchEncodingKind.ShiftJis)]
    [InlineData("shift_jis", SearchEncodingKind.ShiftJis)]
    [InlineData("sjis", SearchEncodingKind.ShiftJis)]
    [InlineData("windows-31j", SearchEncodingKind.ShiftJis)]
    [InlineData("x-sjis", SearchEncodingKind.ShiftJis)]
    [InlineData("866", SearchEncodingKind.Ibm866)]
    [InlineData("cp866", SearchEncodingKind.Ibm866)]
    [InlineData("csibm866", SearchEncodingKind.Ibm866)]
    [InlineData("ibm866", SearchEncodingKind.Ibm866)]
    [InlineData("csisolatin2", SearchEncodingKind.Iso88592)]
    [InlineData("iso-8859-2", SearchEncodingKind.Iso88592)]
    [InlineData("iso-ir-101", SearchEncodingKind.Iso88592)]
    [InlineData("iso8859-2", SearchEncodingKind.Iso88592)]
    [InlineData("iso88592", SearchEncodingKind.Iso88592)]
    [InlineData("iso_8859-2", SearchEncodingKind.Iso88592)]
    [InlineData("iso_8859-2:1987", SearchEncodingKind.Iso88592)]
    [InlineData("l2", SearchEncodingKind.Iso88592)]
    [InlineData("latin2", SearchEncodingKind.Iso88592)]
    [InlineData("csisolatin3", SearchEncodingKind.Iso88593)]
    [InlineData("iso-8859-3", SearchEncodingKind.Iso88593)]
    [InlineData("iso-ir-109", SearchEncodingKind.Iso88593)]
    [InlineData("iso8859-3", SearchEncodingKind.Iso88593)]
    [InlineData("iso88593", SearchEncodingKind.Iso88593)]
    [InlineData("iso_8859-3", SearchEncodingKind.Iso88593)]
    [InlineData("iso_8859-3:1988", SearchEncodingKind.Iso88593)]
    [InlineData("l3", SearchEncodingKind.Iso88593)]
    [InlineData("latin3", SearchEncodingKind.Iso88593)]
    [InlineData("csisolatin4", SearchEncodingKind.Iso88594)]
    [InlineData("iso-8859-4", SearchEncodingKind.Iso88594)]
    [InlineData("iso-ir-110", SearchEncodingKind.Iso88594)]
    [InlineData("iso8859-4", SearchEncodingKind.Iso88594)]
    [InlineData("iso88594", SearchEncodingKind.Iso88594)]
    [InlineData("iso_8859-4", SearchEncodingKind.Iso88594)]
    [InlineData("iso_8859-4:1988", SearchEncodingKind.Iso88594)]
    [InlineData("l4", SearchEncodingKind.Iso88594)]
    [InlineData("latin4", SearchEncodingKind.Iso88594)]
    [InlineData("csisolatincyrillic", SearchEncodingKind.Iso88595)]
    [InlineData("cyrillic", SearchEncodingKind.Iso88595)]
    [InlineData("iso-8859-5", SearchEncodingKind.Iso88595)]
    [InlineData("iso-ir-144", SearchEncodingKind.Iso88595)]
    [InlineData("iso8859-5", SearchEncodingKind.Iso88595)]
    [InlineData("iso88595", SearchEncodingKind.Iso88595)]
    [InlineData("iso_8859-5", SearchEncodingKind.Iso88595)]
    [InlineData("iso_8859-5:1988", SearchEncodingKind.Iso88595)]
    [InlineData("arabic", SearchEncodingKind.Iso88596)]
    [InlineData("asmo-708", SearchEncodingKind.Iso88596)]
    [InlineData("csiso88596e", SearchEncodingKind.Iso88596)]
    [InlineData("csiso88596i", SearchEncodingKind.Iso88596)]
    [InlineData("csisolatinarabic", SearchEncodingKind.Iso88596)]
    [InlineData("ecma-114", SearchEncodingKind.Iso88596)]
    [InlineData("iso-8859-6", SearchEncodingKind.Iso88596)]
    [InlineData("iso-8859-6-e", SearchEncodingKind.Iso88596)]
    [InlineData("iso-8859-6-i", SearchEncodingKind.Iso88596)]
    [InlineData("iso-ir-127", SearchEncodingKind.Iso88596)]
    [InlineData("iso8859-6", SearchEncodingKind.Iso88596)]
    [InlineData("iso88596", SearchEncodingKind.Iso88596)]
    [InlineData("iso_8859-6", SearchEncodingKind.Iso88596)]
    [InlineData("iso_8859-6:1987", SearchEncodingKind.Iso88596)]
    [InlineData("csisolatingreek", SearchEncodingKind.Iso88597)]
    [InlineData("ecma-118", SearchEncodingKind.Iso88597)]
    [InlineData("elot_928", SearchEncodingKind.Iso88597)]
    [InlineData("greek", SearchEncodingKind.Iso88597)]
    [InlineData("greek8", SearchEncodingKind.Iso88597)]
    [InlineData("iso-8859-7", SearchEncodingKind.Iso88597)]
    [InlineData("iso-ir-126", SearchEncodingKind.Iso88597)]
    [InlineData("iso8859-7", SearchEncodingKind.Iso88597)]
    [InlineData("iso88597", SearchEncodingKind.Iso88597)]
    [InlineData("iso_8859-7", SearchEncodingKind.Iso88597)]
    [InlineData("iso_8859-7:1987", SearchEncodingKind.Iso88597)]
    [InlineData("sun_eu_greek", SearchEncodingKind.Iso88597)]
    [InlineData("csiso88598e", SearchEncodingKind.Iso88598)]
    [InlineData("csisolatinhebrew", SearchEncodingKind.Iso88598)]
    [InlineData("hebrew", SearchEncodingKind.Iso88598)]
    [InlineData("iso-8859-8", SearchEncodingKind.Iso88598)]
    [InlineData("iso-8859-8-e", SearchEncodingKind.Iso88598)]
    [InlineData("iso-ir-138", SearchEncodingKind.Iso88598)]
    [InlineData("iso8859-8", SearchEncodingKind.Iso88598)]
    [InlineData("iso88598", SearchEncodingKind.Iso88598)]
    [InlineData("iso_8859-8", SearchEncodingKind.Iso88598)]
    [InlineData("iso_8859-8:1988", SearchEncodingKind.Iso88598)]
    [InlineData("visual", SearchEncodingKind.Iso88598)]
    [InlineData("csiso88598i", SearchEncodingKind.Iso88598I)]
    [InlineData("iso-8859-8-i", SearchEncodingKind.Iso88598I)]
    [InlineData("logical", SearchEncodingKind.Iso88598I)]
    [InlineData("csisolatin6", SearchEncodingKind.Iso885910)]
    [InlineData("iso-8859-10", SearchEncodingKind.Iso885910)]
    [InlineData("iso-ir-157", SearchEncodingKind.Iso885910)]
    [InlineData("iso8859-10", SearchEncodingKind.Iso885910)]
    [InlineData("iso885910", SearchEncodingKind.Iso885910)]
    [InlineData("l6", SearchEncodingKind.Iso885910)]
    [InlineData("latin6", SearchEncodingKind.Iso885910)]
    [InlineData("iso-8859-13", SearchEncodingKind.Iso885913)]
    [InlineData("iso8859-13", SearchEncodingKind.Iso885913)]
    [InlineData("iso885913", SearchEncodingKind.Iso885913)]
    [InlineData("iso-8859-14", SearchEncodingKind.Iso885914)]
    [InlineData("iso8859-14", SearchEncodingKind.Iso885914)]
    [InlineData("iso885914", SearchEncodingKind.Iso885914)]
    [InlineData("csisolatin9", SearchEncodingKind.Iso885915)]
    [InlineData("iso-8859-15", SearchEncodingKind.Iso885915)]
    [InlineData("iso8859-15", SearchEncodingKind.Iso885915)]
    [InlineData("iso885915", SearchEncodingKind.Iso885915)]
    [InlineData("iso_8859-15", SearchEncodingKind.Iso885915)]
    [InlineData("l9", SearchEncodingKind.Iso885915)]
    [InlineData("iso-8859-16", SearchEncodingKind.Iso885916)]
    [InlineData("csiso2022jp", SearchEncodingKind.Iso2022Jp)]
    [InlineData("iso-2022-jp", SearchEncodingKind.Iso2022Jp)]
    [InlineData("cskoi8r", SearchEncodingKind.Koi8R)]
    [InlineData("koi", SearchEncodingKind.Koi8R)]
    [InlineData("koi8", SearchEncodingKind.Koi8R)]
    [InlineData("koi8-r", SearchEncodingKind.Koi8R)]
    [InlineData("koi8_r", SearchEncodingKind.Koi8R)]
    [InlineData("koi8-ru", SearchEncodingKind.Koi8U)]
    [InlineData("koi8-u", SearchEncodingKind.Koi8U)]
    [InlineData("csmacintosh", SearchEncodingKind.Macintosh)]
    [InlineData("mac", SearchEncodingKind.Macintosh)]
    [InlineData("macintosh", SearchEncodingKind.Macintosh)]
    [InlineData("x-mac-roman", SearchEncodingKind.Macintosh)]
    [InlineData("dos-874", SearchEncodingKind.Windows874)]
    [InlineData("iso-8859-11", SearchEncodingKind.Windows874)]
    [InlineData("iso8859-11", SearchEncodingKind.Windows874)]
    [InlineData("iso885911", SearchEncodingKind.Windows874)]
    [InlineData("tis-620", SearchEncodingKind.Windows874)]
    [InlineData("windows-874", SearchEncodingKind.Windows874)]
    [InlineData("cp1250", SearchEncodingKind.Windows1250)]
    [InlineData("windows-1250", SearchEncodingKind.Windows1250)]
    [InlineData("x-cp1250", SearchEncodingKind.Windows1250)]
    [InlineData("cp1251", SearchEncodingKind.Windows1251)]
    [InlineData("windows-1251", SearchEncodingKind.Windows1251)]
    [InlineData("x-cp1251", SearchEncodingKind.Windows1251)]
    [InlineData("ansi_x3.4-1968", SearchEncodingKind.Windows1252)]
    [InlineData("ascii", SearchEncodingKind.Windows1252)]
    [InlineData("cp1252", SearchEncodingKind.Windows1252)]
    [InlineData("cp819", SearchEncodingKind.Windows1252)]
    [InlineData("csisolatin1", SearchEncodingKind.Windows1252)]
    [InlineData("ibm819", SearchEncodingKind.Windows1252)]
    [InlineData("iso-8859-1", SearchEncodingKind.Windows1252)]
    [InlineData("iso-ir-100", SearchEncodingKind.Windows1252)]
    [InlineData("iso8859-1", SearchEncodingKind.Windows1252)]
    [InlineData("iso88591", SearchEncodingKind.Windows1252)]
    [InlineData("iso_8859-1", SearchEncodingKind.Windows1252)]
    [InlineData("iso_8859-1:1987", SearchEncodingKind.Windows1252)]
    [InlineData("l1", SearchEncodingKind.Windows1252)]
    [InlineData(" latin1 ", SearchEncodingKind.Windows1252)]
    [InlineData("us-ascii", SearchEncodingKind.Windows1252)]
    [InlineData("ANSI_X3.4-1968", SearchEncodingKind.Windows1252)]
    [InlineData("windows-1252", SearchEncodingKind.Windows1252)]
    [InlineData("x-cp1252", SearchEncodingKind.Windows1252)]
    [InlineData("cp1253", SearchEncodingKind.Windows1253)]
    [InlineData("windows-1253", SearchEncodingKind.Windows1253)]
    [InlineData("x-cp1253", SearchEncodingKind.Windows1253)]
    [InlineData("cp1254", SearchEncodingKind.Windows1254)]
    [InlineData("csisolatin5", SearchEncodingKind.Windows1254)]
    [InlineData("iso-8859-9", SearchEncodingKind.Windows1254)]
    [InlineData("iso-ir-148", SearchEncodingKind.Windows1254)]
    [InlineData("iso8859-9", SearchEncodingKind.Windows1254)]
    [InlineData("iso88599", SearchEncodingKind.Windows1254)]
    [InlineData("iso_8859-9", SearchEncodingKind.Windows1254)]
    [InlineData("iso_8859-9:1989", SearchEncodingKind.Windows1254)]
    [InlineData("l5", SearchEncodingKind.Windows1254)]
    [InlineData("latin5", SearchEncodingKind.Windows1254)]
    [InlineData("windows-1254", SearchEncodingKind.Windows1254)]
    [InlineData("x-cp1254", SearchEncodingKind.Windows1254)]
    [InlineData("cp1255", SearchEncodingKind.Windows1255)]
    [InlineData("windows-1255", SearchEncodingKind.Windows1255)]
    [InlineData("x-cp1255", SearchEncodingKind.Windows1255)]
    [InlineData("cp1256", SearchEncodingKind.Windows1256)]
    [InlineData("windows-1256", SearchEncodingKind.Windows1256)]
    [InlineData("x-cp1256", SearchEncodingKind.Windows1256)]
    [InlineData("cp1257", SearchEncodingKind.Windows1257)]
    [InlineData("windows-1257", SearchEncodingKind.Windows1257)]
    [InlineData("x-cp1257", SearchEncodingKind.Windows1257)]
    [InlineData("cp1258", SearchEncodingKind.Windows1258)]
    [InlineData("windows-1258", SearchEncodingKind.Windows1258)]
    [InlineData("x-cp1258", SearchEncodingKind.Windows1258)]
    [InlineData("x-mac-cyrillic", SearchEncodingKind.XMacCyrillic)]
    [InlineData("x-mac-ukrainian", SearchEncodingKind.XMacCyrillic)]
    [InlineData("x-user-defined", SearchEncodingKind.XUserDefined)]
    public void TryGetKindMatchesImplementedEncodingRsLabels(string label, SearchEncodingKind expectedEncodingKind)
    {
        bool resolved = SearchEncodingLabel.TryGetKind(label, out SearchEncodingKind encodingKind);

        Assert.True(resolved);
        Assert.Equal(expectedEncodingKind, encodingKind);
    }

    /// <summary>
    /// Verifies special modes and unsupported WHATWG labels are not resolved as encodings.
    /// </summary>
    /// <param name="label">The input encoding label.</param>
    [Theory]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("replacement")]
    [InlineData("hz-gb-2312")]
    [InlineData("iso-2022-kr")]
    public void TryGetKindRejectsSpecialOrUnsupportedLabels(string label)
    {
        bool resolved = SearchEncodingLabel.TryGetKind(label, out SearchEncodingKind encodingKind);

        Assert.False(resolved);
        Assert.Equal(default, encodingKind);
    }

    /// <summary>
    /// Verifies raw mode preserves the caller's byte array instance.
    /// </summary>
    [Fact]
    public void NoneReturnsOriginalBytes()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'n'];

        byte[] decoded = SearchEncoding.Decode(bytes, SearchEncodingKind.None);

        Assert.Same(bytes, decoded);
    }

    /// <summary>
    /// Verifies automatic mode sniffs byte-order marks and otherwise preserves bytes.
    /// </summary>
    [Fact]
    public void AutoSniffsBomAndOtherwisePreservesBytes()
    {
        Assert.Equal("needle"u8.ToArray(), SearchEncoding.Decode([0xEF, 0xBB, 0xBF, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e'], SearchEncodingKind.Auto));
        Assert.Equal("needle\n"u8.ToArray(), SearchEncoding.Decode([0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0], SearchEncodingKind.Auto));
        Assert.Equal([0xff, (byte)'n'], SearchEncoding.Decode([0xff, (byte)'n'], SearchEncodingKind.Auto));
    }

    /// <summary>
    /// Verifies explicit UTF-16 modes transcode little-endian and big-endian input.
    /// </summary>
    [Fact]
    public void ExplicitUtf16ModesTranscodeToUtf8()
    {
        Assert.Equal("A\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([(byte)'A', 0, 0x3d, 0xd8, 0x00, 0xde], SearchEncodingKind.Utf16Le));
        Assert.Equal("A\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0, (byte)'A', 0xd8, 0x3d, 0xde, 0x00], SearchEncodingKind.Utf16Be));
    }

    /// <summary>
    /// Verifies malformed UTF-16 input uses replacement characters.
    /// </summary>
    [Fact]
    public void Utf16ReplacesMalformedUnits()
    {
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x3d, 0xd8, (byte)'A', 0], SearchEncodingKind.Utf16Le));
        Assert.Equal("A\uFFFD"u8.ToArray(), SearchEncoding.Decode([(byte)'A', 0, 0x00], SearchEncodingKind.Utf16Le));
    }

    /// <summary>
    /// Verifies explicit UTF-8 mode replaces malformed byte sequences.
    /// </summary>
    [Fact]
    public void Utf8ReplacesMalformedSequences()
    {
        Assert.Equal("a\uFFFDb"u8.ToArray(), SearchEncoding.Decode([(byte)'a', 0xff, (byte)'b'], SearchEncodingKind.Utf8));
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0xe2, (byte)'A'], SearchEncodingKind.Utf8));
        Assert.Equal("\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0xf0, 0x9f, 0x98, 0x80], SearchEncodingKind.Utf8));
    }

    /// <summary>
    /// Verifies ISO-2022-JP bytes decode using the WHATWG state machine.
    /// </summary>
    [Fact]
    public void Iso2022JpDecodesEscapedJapaneseRomanAndKatakana()
    {
        Assert.Equal(
            "\u65E5\u672C\u8A9E"u8.ToArray(),
            SearchEncoding.Decode([0x1B, (byte)'$', (byte)'B', 0x46, 0x7C, 0x4B, 0x5C, 0x38, 0x6C, 0x1B, (byte)'(', (byte)'B'], SearchEncodingKind.Iso2022Jp));
        Assert.Equal(
            "\u00A5\u203E\uFF76"u8.ToArray(),
            SearchEncoding.Decode([0x1B, (byte)'(', (byte)'J', (byte)'\\', (byte)'~', 0x1B, (byte)'(', (byte)'I', (byte)'6'], SearchEncodingKind.Iso2022Jp));
    }

    /// <summary>
    /// Verifies ISO-2022-JP malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void Iso2022JpReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFDxA"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'x', (byte)'A'], SearchEncodingKind.Iso2022Jp));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'$', (byte)'B', 0x24], SearchEncodingKind.Iso2022Jp));
        Assert.Equal("\uFFFD$"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'$'], SearchEncodingKind.Iso2022Jp));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.Iso2022Jp));
    }

    /// <summary>
    /// Verifies EUC-JP bytes decode using the WHATWG mapping.
    /// </summary>
    [Fact]
    public void EucJpDecodesJapaneseJis0212AndHalfWidth()
    {
        Assert.Equal("\u65E5\u672C\u8A9E"u8.ToArray(), SearchEncoding.Decode([0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC], SearchEncodingKind.EucJp));
        Assert.Equal("\u3042\u30A2\uFF76"u8.ToArray(), SearchEncoding.Decode([0xA4, 0xA2, 0xA5, 0xA2, 0x8E, 0xB6], SearchEncodingKind.EucJp));
        Assert.Equal("\u4E02\u02D8"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0, 0xA1, 0x8F, 0xA2, 0xAF], SearchEncodingKind.EucJp));
    }

    /// <summary>
    /// Verifies EUC-JP malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void EucJpReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0xA4, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x8E, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0], SearchEncodingKind.EucJp));
    }

    /// <summary>
    /// Verifies Shift_JIS bytes decode using the WHATWG mapping.
    /// </summary>
    [Fact]
    public void ShiftJisDecodesJapaneseAndSpecialSingles()
    {
        Assert.Equal("\u65E5\u672C\u8A9E"u8.ToArray(), SearchEncoding.Decode([0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA], SearchEncodingKind.ShiftJis));
        Assert.Equal("\u3042\u30A2\uFF76"u8.ToArray(), SearchEncoding.Decode([0x82, 0xA0, 0x83, 0x41, 0xB6], SearchEncodingKind.ShiftJis));
        Assert.Equal("\u0080"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.ShiftJis));
    }

    /// <summary>
    /// Verifies Shift_JIS malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void ShiftJisReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x82, (byte)'0'], SearchEncodingKind.ShiftJis));
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x82, 0xFD, (byte)'A'], SearchEncodingKind.ShiftJis));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x82], SearchEncodingKind.ShiftJis));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0xFD], SearchEncodingKind.ShiftJis));
    }

    /// <summary>
    /// Verifies Big5 bytes decode using the WHATWG mapping.
    /// </summary>
    [Fact]
    public void Big5DecodesBmpAstralAndCombinations()
    {
        Assert.Equal("\u4E2D\u6587"u8.ToArray(), SearchEncoding.Decode([0xA4, 0xA4, 0xA4, 0xE5], SearchEncodingKind.Big5));
        Assert.Equal("\U00027267"u8.ToArray(), SearchEncoding.Decode([0x87, 0x45], SearchEncodingKind.Big5));
        Assert.Equal("\u00CA\u0304"u8.ToArray(), SearchEncoding.Decode([0x88, 0x62], SearchEncodingKind.Big5));
    }

    /// <summary>
    /// Verifies Big5 malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void Big5ReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x88, (byte)'0'], SearchEncodingKind.Big5));
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x80, (byte)'A'], SearchEncodingKind.Big5));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x88], SearchEncodingKind.Big5));
    }

    /// <summary>
    /// Verifies GBK and GB18030 bytes decode using the WHATWG mapping.
    /// </summary>
    [Fact]
    public void Gb18030DecodesGbkAndFourByteRanges()
    {
        Assert.Equal("\u20AC\u4F60\u597D"u8.ToArray(), SearchEncoding.Decode([0x80, 0xC4, 0xE3, 0xBA, 0xC3], SearchEncodingKind.Gbk));
        Assert.Equal("\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0x94, 0x39, 0xFC, 0x36], SearchEncodingKind.Gb18030));
    }

    /// <summary>
    /// Verifies GB18030 malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void Gb18030ReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFD0A"u8.ToArray(), SearchEncoding.Decode([0x81, 0x30, (byte)'A'], SearchEncodingKind.Gb18030));
        Assert.Equal("\uFFFD/"u8.ToArray(), SearchEncoding.Decode([0x81, (byte)'/'], SearchEncodingKind.Gbk));
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x84, 0x39, 0x81, 0x30, (byte)'A'], SearchEncodingKind.Gb18030));
    }

    /// <summary>
    /// Verifies EUC-KR bytes decode using the WHATWG mapping.
    /// </summary>
    [Fact]
    public void EucKrDecodesKsx1001AndCp949Extensions()
    {
        Assert.Equal("\uAC00\uB098\uB2E4"u8.ToArray(), SearchEncoding.Decode([0xB0, 0xA1, 0xB3, 0xAA, 0xB4, 0xD9], SearchEncodingKind.EucKr));
        Assert.Equal("\uAC02"u8.ToArray(), SearchEncoding.Decode([0x81, 0x41], SearchEncodingKind.EucKr));
    }

    /// <summary>
    /// Verifies EUC-KR malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [Fact]
    public void EucKrReplacesMalformedSequences()
    {
        Assert.Equal("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0xB0, (byte)'0'], SearchEncodingKind.EucKr));
        Assert.Equal("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0xB0, 0x80, (byte)'A'], SearchEncodingKind.EucKr));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0xB0], SearchEncodingKind.EucKr));
        Assert.Equal("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.EucKr));
    }

    /// <summary>
    /// Verifies Windows-1252 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1252DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0x82, 0x81, 0xA0, 0xFF], SearchEncodingKind.Windows1252);

        Assert.Equal("\u20AC\u201A\u0081\u00A0\u00FF"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies IBM866 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Ibm866DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x8F, 0xAF, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2], SearchEncodingKind.Ibm866);

        Assert.Equal("\u041F\u043F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-2 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88592DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xC6, 0xE6, 0xFF], SearchEncodingKind.Iso88592);

        Assert.Equal("\u0104\u0105\u0106\u0107\u02D9"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-3 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88593DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA5, 0xB1], SearchEncodingKind.Iso88593);

        Assert.Equal("\u0126\uFFFD\u0127"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-4 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88594DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xC7, 0xE7], SearchEncodingKind.Iso88594);

        Assert.Equal("\u0104\u0105\u012E\u012F"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-5 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88595DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB0, 0xD0, 0xF0], SearchEncodingKind.Iso88595);

        Assert.Equal("\u0401\u0410\u0430\u2116"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-6 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88596DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC7, 0xE3, 0xA1], SearchEncodingKind.Iso88596);

        Assert.Equal("\u0627\u0643\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-7 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88597DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC1, 0xE1, 0xAE], SearchEncodingKind.Iso88597);

        Assert.Equal("\u0391\u03B1\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-8 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88598DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA, 0xA1], SearchEncodingKind.Iso88598);

        Assert.Equal("\u05D0\u05EA\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-8-I bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso88598IDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA], SearchEncodingKind.Iso88598I);

        Assert.Equal("\u05D0\u05EA"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-10 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso885910DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xFF], SearchEncodingKind.Iso885910);

        Assert.Equal("\u0104\u0105\u0138"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-13 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso885913DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB4, 0xFF], SearchEncodingKind.Iso885913);

        Assert.Equal("\u201D\u201C\u2019"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-14 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso885914DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA2, 0xD0, 0xF0], SearchEncodingKind.Iso885914);

        Assert.Equal("\u1E02\u1E03\u0174\u0175"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-15 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso885915DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA4, 0xBC, 0xBE], SearchEncodingKind.Iso885915);

        Assert.Equal("\u20AC\u0152\u0178"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-16 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Iso885916DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA2, 0xAA, 0xFE], SearchEncodingKind.Iso885916);

        Assert.Equal("\u0104\u0105\u0218\u021B"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies KOI8-R bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Koi8RDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xF0, 0xD2, 0xC9, 0xD7, 0xC5, 0xD4], SearchEncodingKind.Koi8R);

        Assert.Equal("\u041F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies KOI8-U bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Koi8UDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA4, 0xB4], SearchEncodingKind.Koi8U);

        Assert.Equal("\u0454\u0404"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Macintosh bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void MacintoshDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0x8E, 0xDB], SearchEncodingKind.Macintosh);

        Assert.Equal("\u00C4\u00E9\u20AC"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-874 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows874DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xDF, 0xFB], SearchEncodingKind.Windows874);

        Assert.Equal("\u0E01\u0E3F\u0E5B"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1250 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1250DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x8C, 0x9C, 0xA5, 0xB9], SearchEncodingKind.Windows1250);

        Assert.Equal("\u015A\u015B\u0104\u0105"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1251 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1251DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x88, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2], SearchEncodingKind.Windows1251);

        Assert.Equal("\u20AC\u041F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1253 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1253DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xAA, 0xC1, 0xE1], SearchEncodingKind.Windows1253);

        Assert.Equal("\uFFFD\u0391\u03B1"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1254 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1254DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xD0, 0xDD, 0xFD, 0xFE], SearchEncodingKind.Windows1254);

        Assert.Equal("\u011E\u0130\u0131\u015F"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1255 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1255DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA, 0xC0], SearchEncodingKind.Windows1255);

        Assert.Equal("\u05D0\u05EA\u05B0"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1256 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1256DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC7, 0xE3, 0xED], SearchEncodingKind.Windows1256);

        Assert.Equal("\u0627\u0645\u064A"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1257 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1257DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC0, 0xE0, 0xA1], SearchEncodingKind.Windows1257);

        Assert.Equal("\u0104\u0105\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1258 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void Windows1258DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xCC, 0xD2, 0xF2, 0xFE], SearchEncodingKind.Windows1258);

        Assert.Equal("\u0300\u0309\u0323\u20AB"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies x-mac-cyrillic bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [Fact]
    public void XMacCyrillicDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0xDF, 0xFF], SearchEncodingKind.XMacCyrillic);

        Assert.Equal("\u0410\u044F\u20AC"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies x-user-defined bytes decode using the WHATWG private-use mapping.
    /// </summary>
    [Fact]
    public void XUserDefinedDecodesPrivateUseMapping()
    {
        byte[] decoded = SearchEncoding.Decode([(byte)'A', 0x80, 0xFF], SearchEncodingKind.XUserDefined);

        Assert.Equal("A\uF780\uF7FF"u8.ToArray(), decoded);
    }
}
