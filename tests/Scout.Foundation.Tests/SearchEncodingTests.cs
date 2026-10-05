using System.Security.Cryptography;

namespace Scout;

/// <summary>
/// Verifies search input transcoding behavior.
/// </summary>
[TestClass]
public sealed class SearchEncodingTests
{
    private static readonly string EncodingRsLabelTestsPath = Path.Join(FindRepositoryRoot(), "upstream", "encoding_rs-0.8.35", "src", "test_labels_names.rs");

    /// <summary>
    /// Verifies implemented WHATWG labels resolve to the expected search encoding kinds.
    /// </summary>
    /// <param name="label">The input encoding label.</param>
    /// <param name="expectedEncodingKind">The expected search encoding kind.</param>
    [TestMethod]
    [DataRow("utf-8", SearchEncodingKind.Utf8)]
    [DataRow("utf8", SearchEncodingKind.Utf8)]
    [DataRow("unicode20utf8", SearchEncodingKind.Utf8)]
    [DataRow("unicode11utf8", SearchEncodingKind.Utf8)]
    [DataRow("x-unicode20utf8", SearchEncodingKind.Utf8)]
    [DataRow("unicode-1-1-utf-8", SearchEncodingKind.Utf8)]
    [DataRow("utf-16", SearchEncodingKind.Utf16)]
    [DataRow("csunicode", SearchEncodingKind.Utf16Le)]
    [DataRow("iso-10646-ucs-2", SearchEncodingKind.Utf16Le)]
    [DataRow("ucs-2", SearchEncodingKind.Utf16Le)]
    [DataRow("unicode", SearchEncodingKind.Utf16Le)]
    [DataRow("unicodefeff", SearchEncodingKind.Utf16Le)]
    [DataRow("utf-16le", SearchEncodingKind.Utf16Le)]
    [DataRow("utf-16be", SearchEncodingKind.Utf16Be)]
    [DataRow("unicodefffe", SearchEncodingKind.Utf16Be)]
    [DataRow("korean", SearchEncodingKind.EucKr)]
    [DataRow("euc-kr", SearchEncodingKind.EucKr)]
    [DataRow("ksc5601", SearchEncodingKind.EucKr)]
    [DataRow("cseuckr", SearchEncodingKind.EucKr)]
    [DataRow("ksc_5601", SearchEncodingKind.EucKr)]
    [DataRow("iso-ir-149", SearchEncodingKind.EucKr)]
    [DataRow("windows-949", SearchEncodingKind.EucKr)]
    [DataRow("csksc56011987", SearchEncodingKind.EucKr)]
    [DataRow("ks_c_5601-1987", SearchEncodingKind.EucKr)]
    [DataRow("ks_c_5601-1989", SearchEncodingKind.EucKr)]
    [DataRow("cseucpkdfmtjapanese", SearchEncodingKind.EucJp)]
    [DataRow("euc-jp", SearchEncodingKind.EucJp)]
    [DataRow("x-euc-jp", SearchEncodingKind.EucJp)]
    [DataRow("big5", SearchEncodingKind.Big5)]
    [DataRow("big5-hkscs", SearchEncodingKind.Big5)]
    [DataRow("cn-big5", SearchEncodingKind.Big5)]
    [DataRow("csbig5", SearchEncodingKind.Big5)]
    [DataRow("x-x-big5", SearchEncodingKind.Big5)]
    [DataRow("gb18030", SearchEncodingKind.Gb18030)]
    [DataRow("chinese", SearchEncodingKind.Gbk)]
    [DataRow("csgb2312", SearchEncodingKind.Gbk)]
    [DataRow("csiso58gb231280", SearchEncodingKind.Gbk)]
    [DataRow("gb2312", SearchEncodingKind.Gbk)]
    [DataRow("gb_2312", SearchEncodingKind.Gbk)]
    [DataRow("gb_2312-80", SearchEncodingKind.Gbk)]
    [DataRow("gbk", SearchEncodingKind.Gbk)]
    [DataRow("iso-ir-58", SearchEncodingKind.Gbk)]
    [DataRow("x-gbk", SearchEncodingKind.Gbk)]
    [DataRow("csshiftjis", SearchEncodingKind.ShiftJis)]
    [DataRow("ms932", SearchEncodingKind.ShiftJis)]
    [DataRow("ms_kanji", SearchEncodingKind.ShiftJis)]
    [DataRow("shift-jis", SearchEncodingKind.ShiftJis)]
    [DataRow("shift_jis", SearchEncodingKind.ShiftJis)]
    [DataRow("sjis", SearchEncodingKind.ShiftJis)]
    [DataRow("windows-31j", SearchEncodingKind.ShiftJis)]
    [DataRow("x-sjis", SearchEncodingKind.ShiftJis)]
    [DataRow("866", SearchEncodingKind.Ibm866)]
    [DataRow("cp866", SearchEncodingKind.Ibm866)]
    [DataRow("csibm866", SearchEncodingKind.Ibm866)]
    [DataRow("ibm866", SearchEncodingKind.Ibm866)]
    [DataRow("csisolatin2", SearchEncodingKind.Iso88592)]
    [DataRow("iso-8859-2", SearchEncodingKind.Iso88592)]
    [DataRow("iso-ir-101", SearchEncodingKind.Iso88592)]
    [DataRow("iso8859-2", SearchEncodingKind.Iso88592)]
    [DataRow("iso88592", SearchEncodingKind.Iso88592)]
    [DataRow("iso_8859-2", SearchEncodingKind.Iso88592)]
    [DataRow("iso_8859-2:1987", SearchEncodingKind.Iso88592)]
    [DataRow("l2", SearchEncodingKind.Iso88592)]
    [DataRow("latin2", SearchEncodingKind.Iso88592)]
    [DataRow("csisolatin3", SearchEncodingKind.Iso88593)]
    [DataRow("iso-8859-3", SearchEncodingKind.Iso88593)]
    [DataRow("iso-ir-109", SearchEncodingKind.Iso88593)]
    [DataRow("iso8859-3", SearchEncodingKind.Iso88593)]
    [DataRow("iso88593", SearchEncodingKind.Iso88593)]
    [DataRow("iso_8859-3", SearchEncodingKind.Iso88593)]
    [DataRow("iso_8859-3:1988", SearchEncodingKind.Iso88593)]
    [DataRow("l3", SearchEncodingKind.Iso88593)]
    [DataRow("latin3", SearchEncodingKind.Iso88593)]
    [DataRow("csisolatin4", SearchEncodingKind.Iso88594)]
    [DataRow("iso-8859-4", SearchEncodingKind.Iso88594)]
    [DataRow("iso-ir-110", SearchEncodingKind.Iso88594)]
    [DataRow("iso8859-4", SearchEncodingKind.Iso88594)]
    [DataRow("iso88594", SearchEncodingKind.Iso88594)]
    [DataRow("iso_8859-4", SearchEncodingKind.Iso88594)]
    [DataRow("iso_8859-4:1988", SearchEncodingKind.Iso88594)]
    [DataRow("l4", SearchEncodingKind.Iso88594)]
    [DataRow("latin4", SearchEncodingKind.Iso88594)]
    [DataRow("csisolatincyrillic", SearchEncodingKind.Iso88595)]
    [DataRow("cyrillic", SearchEncodingKind.Iso88595)]
    [DataRow("iso-8859-5", SearchEncodingKind.Iso88595)]
    [DataRow("iso-ir-144", SearchEncodingKind.Iso88595)]
    [DataRow("iso8859-5", SearchEncodingKind.Iso88595)]
    [DataRow("iso88595", SearchEncodingKind.Iso88595)]
    [DataRow("iso_8859-5", SearchEncodingKind.Iso88595)]
    [DataRow("iso_8859-5:1988", SearchEncodingKind.Iso88595)]
    [DataRow("arabic", SearchEncodingKind.Iso88596)]
    [DataRow("asmo-708", SearchEncodingKind.Iso88596)]
    [DataRow("csiso88596e", SearchEncodingKind.Iso88596)]
    [DataRow("csiso88596i", SearchEncodingKind.Iso88596)]
    [DataRow("csisolatinarabic", SearchEncodingKind.Iso88596)]
    [DataRow("ecma-114", SearchEncodingKind.Iso88596)]
    [DataRow("iso-8859-6", SearchEncodingKind.Iso88596)]
    [DataRow("iso-8859-6-e", SearchEncodingKind.Iso88596)]
    [DataRow("iso-8859-6-i", SearchEncodingKind.Iso88596)]
    [DataRow("iso-ir-127", SearchEncodingKind.Iso88596)]
    [DataRow("iso8859-6", SearchEncodingKind.Iso88596)]
    [DataRow("iso88596", SearchEncodingKind.Iso88596)]
    [DataRow("iso_8859-6", SearchEncodingKind.Iso88596)]
    [DataRow("iso_8859-6:1987", SearchEncodingKind.Iso88596)]
    [DataRow("csisolatingreek", SearchEncodingKind.Iso88597)]
    [DataRow("ecma-118", SearchEncodingKind.Iso88597)]
    [DataRow("elot_928", SearchEncodingKind.Iso88597)]
    [DataRow("greek", SearchEncodingKind.Iso88597)]
    [DataRow("greek8", SearchEncodingKind.Iso88597)]
    [DataRow("iso-8859-7", SearchEncodingKind.Iso88597)]
    [DataRow("iso-ir-126", SearchEncodingKind.Iso88597)]
    [DataRow("iso8859-7", SearchEncodingKind.Iso88597)]
    [DataRow("iso88597", SearchEncodingKind.Iso88597)]
    [DataRow("iso_8859-7", SearchEncodingKind.Iso88597)]
    [DataRow("iso_8859-7:1987", SearchEncodingKind.Iso88597)]
    [DataRow("sun_eu_greek", SearchEncodingKind.Iso88597)]
    [DataRow("csiso88598e", SearchEncodingKind.Iso88598)]
    [DataRow("csisolatinhebrew", SearchEncodingKind.Iso88598)]
    [DataRow("hebrew", SearchEncodingKind.Iso88598)]
    [DataRow("iso-8859-8", SearchEncodingKind.Iso88598)]
    [DataRow("iso-8859-8-e", SearchEncodingKind.Iso88598)]
    [DataRow("iso-ir-138", SearchEncodingKind.Iso88598)]
    [DataRow("iso8859-8", SearchEncodingKind.Iso88598)]
    [DataRow("iso88598", SearchEncodingKind.Iso88598)]
    [DataRow("iso_8859-8", SearchEncodingKind.Iso88598)]
    [DataRow("iso_8859-8:1988", SearchEncodingKind.Iso88598)]
    [DataRow("visual", SearchEncodingKind.Iso88598)]
    [DataRow("csiso88598i", SearchEncodingKind.Iso88598I)]
    [DataRow("iso-8859-8-i", SearchEncodingKind.Iso88598I)]
    [DataRow("logical", SearchEncodingKind.Iso88598I)]
    [DataRow("csisolatin6", SearchEncodingKind.Iso885910)]
    [DataRow("iso-8859-10", SearchEncodingKind.Iso885910)]
    [DataRow("iso-ir-157", SearchEncodingKind.Iso885910)]
    [DataRow("iso8859-10", SearchEncodingKind.Iso885910)]
    [DataRow("iso885910", SearchEncodingKind.Iso885910)]
    [DataRow("l6", SearchEncodingKind.Iso885910)]
    [DataRow("latin6", SearchEncodingKind.Iso885910)]
    [DataRow("iso-8859-13", SearchEncodingKind.Iso885913)]
    [DataRow("iso8859-13", SearchEncodingKind.Iso885913)]
    [DataRow("iso885913", SearchEncodingKind.Iso885913)]
    [DataRow("iso-8859-14", SearchEncodingKind.Iso885914)]
    [DataRow("iso8859-14", SearchEncodingKind.Iso885914)]
    [DataRow("iso885914", SearchEncodingKind.Iso885914)]
    [DataRow("csisolatin9", SearchEncodingKind.Iso885915)]
    [DataRow("iso-8859-15", SearchEncodingKind.Iso885915)]
    [DataRow("iso8859-15", SearchEncodingKind.Iso885915)]
    [DataRow("iso885915", SearchEncodingKind.Iso885915)]
    [DataRow("iso_8859-15", SearchEncodingKind.Iso885915)]
    [DataRow("l9", SearchEncodingKind.Iso885915)]
    [DataRow("iso-8859-16", SearchEncodingKind.Iso885916)]
    [DataRow("csiso2022jp", SearchEncodingKind.Iso2022Jp)]
    [DataRow("iso-2022-jp", SearchEncodingKind.Iso2022Jp)]
    [DataRow("cskoi8r", SearchEncodingKind.Koi8R)]
    [DataRow("koi", SearchEncodingKind.Koi8R)]
    [DataRow("koi8", SearchEncodingKind.Koi8R)]
    [DataRow("koi8-r", SearchEncodingKind.Koi8R)]
    [DataRow("koi8_r", SearchEncodingKind.Koi8R)]
    [DataRow("koi8-ru", SearchEncodingKind.Koi8U)]
    [DataRow("koi8-u", SearchEncodingKind.Koi8U)]
    [DataRow("csmacintosh", SearchEncodingKind.Macintosh)]
    [DataRow("mac", SearchEncodingKind.Macintosh)]
    [DataRow("macintosh", SearchEncodingKind.Macintosh)]
    [DataRow("x-mac-roman", SearchEncodingKind.Macintosh)]
    [DataRow("dos-874", SearchEncodingKind.Windows874)]
    [DataRow("iso-8859-11", SearchEncodingKind.Windows874)]
    [DataRow("iso8859-11", SearchEncodingKind.Windows874)]
    [DataRow("iso885911", SearchEncodingKind.Windows874)]
    [DataRow("tis-620", SearchEncodingKind.Windows874)]
    [DataRow("windows-874", SearchEncodingKind.Windows874)]
    [DataRow("cp1250", SearchEncodingKind.Windows1250)]
    [DataRow("windows-1250", SearchEncodingKind.Windows1250)]
    [DataRow("x-cp1250", SearchEncodingKind.Windows1250)]
    [DataRow("cp1251", SearchEncodingKind.Windows1251)]
    [DataRow("windows-1251", SearchEncodingKind.Windows1251)]
    [DataRow("x-cp1251", SearchEncodingKind.Windows1251)]
    [DataRow("ansi_x3.4-1968", SearchEncodingKind.Windows1252)]
    [DataRow("ascii", SearchEncodingKind.Windows1252)]
    [DataRow("cp1252", SearchEncodingKind.Windows1252)]
    [DataRow("cp819", SearchEncodingKind.Windows1252)]
    [DataRow("csisolatin1", SearchEncodingKind.Windows1252)]
    [DataRow("ibm819", SearchEncodingKind.Windows1252)]
    [DataRow("iso-8859-1", SearchEncodingKind.Windows1252)]
    [DataRow("iso-ir-100", SearchEncodingKind.Windows1252)]
    [DataRow("iso8859-1", SearchEncodingKind.Windows1252)]
    [DataRow("iso88591", SearchEncodingKind.Windows1252)]
    [DataRow("iso_8859-1", SearchEncodingKind.Windows1252)]
    [DataRow("iso_8859-1:1987", SearchEncodingKind.Windows1252)]
    [DataRow("l1", SearchEncodingKind.Windows1252)]
    [DataRow(" latin1 ", SearchEncodingKind.Windows1252)]
    [DataRow("us-ascii", SearchEncodingKind.Windows1252)]
    [DataRow("ANSI_X3.4-1968", SearchEncodingKind.Windows1252)]
    [DataRow("windows-1252", SearchEncodingKind.Windows1252)]
    [DataRow("x-cp1252", SearchEncodingKind.Windows1252)]
    [DataRow("cp1253", SearchEncodingKind.Windows1253)]
    [DataRow("windows-1253", SearchEncodingKind.Windows1253)]
    [DataRow("x-cp1253", SearchEncodingKind.Windows1253)]
    [DataRow("cp1254", SearchEncodingKind.Windows1254)]
    [DataRow("csisolatin5", SearchEncodingKind.Windows1254)]
    [DataRow("iso-8859-9", SearchEncodingKind.Windows1254)]
    [DataRow("iso-ir-148", SearchEncodingKind.Windows1254)]
    [DataRow("iso8859-9", SearchEncodingKind.Windows1254)]
    [DataRow("iso88599", SearchEncodingKind.Windows1254)]
    [DataRow("iso_8859-9", SearchEncodingKind.Windows1254)]
    [DataRow("iso_8859-9:1989", SearchEncodingKind.Windows1254)]
    [DataRow("l5", SearchEncodingKind.Windows1254)]
    [DataRow("latin5", SearchEncodingKind.Windows1254)]
    [DataRow("windows-1254", SearchEncodingKind.Windows1254)]
    [DataRow("x-cp1254", SearchEncodingKind.Windows1254)]
    [DataRow("cp1255", SearchEncodingKind.Windows1255)]
    [DataRow("windows-1255", SearchEncodingKind.Windows1255)]
    [DataRow("x-cp1255", SearchEncodingKind.Windows1255)]
    [DataRow("cp1256", SearchEncodingKind.Windows1256)]
    [DataRow("windows-1256", SearchEncodingKind.Windows1256)]
    [DataRow("x-cp1256", SearchEncodingKind.Windows1256)]
    [DataRow("cp1257", SearchEncodingKind.Windows1257)]
    [DataRow("windows-1257", SearchEncodingKind.Windows1257)]
    [DataRow("x-cp1257", SearchEncodingKind.Windows1257)]
    [DataRow("cp1258", SearchEncodingKind.Windows1258)]
    [DataRow("windows-1258", SearchEncodingKind.Windows1258)]
    [DataRow("x-cp1258", SearchEncodingKind.Windows1258)]
    [DataRow("x-mac-cyrillic", SearchEncodingKind.XMacCyrillic)]
    [DataRow("x-mac-ukrainian", SearchEncodingKind.XMacCyrillic)]
    [DataRow("x-user-defined", SearchEncodingKind.XUserDefined)]
    public void TryGetKindMatchesImplementedEncodingRsLabels(string label, SearchEncodingKind expectedEncodingKind)
    {
        bool resolved = SearchEncodingLabel.TryGetKind(label, out SearchEncodingKind encodingKind);

        Assert.IsTrue(resolved);
        Assert.AreEqual(expectedEncodingKind, encodingKind);
    }

    /// <summary>
    /// Verifies special modes and unsupported WHATWG labels are not resolved as encodings.
    /// </summary>
    /// <param name="label">The input encoding label.</param>
    [TestMethod]
    [DataRow("auto")]
    [DataRow("none")]
    [DataRow("replacement")]
    [DataRow("hz-gb-2312")]
    [DataRow("iso-2022-kr")]
    public void TryGetKindRejectsSpecialOrUnsupportedLabels(string label)
    {
        bool resolved = SearchEncodingLabel.TryGetKind(label, out SearchEncodingKind encodingKind);

        Assert.IsFalse(resolved);
        Assert.AreEqual(default, encodingKind);
    }

    /// <summary>
    /// Verifies Scout label resolution tracks <c>encoding_rs::Encoding::for_label_no_replacement</c>.
    /// </summary>
    [TestMethod]
    public void TryGetKindMatchesEncodingRsForLabelNoReplacementCatalog()
    {
        string upstream = File.ReadAllText(EncodingRsLabelTestsPath);
        string prerequisiteLock = File.ReadAllText(Path.Join(FindRepositoryRoot(), "tests", "PREREQS.lock"));
        Assert.Contains("name = \"encoding-rs-0.8.35-labels\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("path = \"upstream/encoding_rs-0.8.35/src/test_labels_names.rs\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.Contains("sha256 = \"23a2e11b02b3b8d15fb5613a625e3edb2c61e70e3c581abfd638719a4088200d\"", prerequisiteLock, StringComparison.Ordinal);
        Assert.AreEqual("23A2E11B02B3B8D15FB5613A625E3EDB2C61E70E3C581ABFD638719A4088200D", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(EncodingRsLabelTestsPath))));

        List<(string Label, string EncodingName)> cases = ReadEncodingRsLabelCases(upstream);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Assert.HasCount(228, cases);
        for (int index = 0; index < cases.Count; index++)
        {
            (string label, string upstreamEncodingName) = cases[index];

            Assert.IsTrue(seen.Add(label), "Duplicate encoding_rs label: " + label);
            bool resolved = SearchEncodingLabel.TryGetKind(label, out SearchEncodingKind encodingKind);
            if (string.Equals(upstreamEncodingName, "REPLACEMENT", StringComparison.Ordinal))
            {
                Assert.IsFalse(resolved, "Replacement-only label should be rejected: " + label);
                Assert.AreEqual(default, encodingKind);
                continue;
            }

            Assert.IsTrue(resolved, "Missing encoding_rs label: " + label);
            Assert.AreEqual(ToSearchEncodingKind(label, upstreamEncodingName), encodingKind);
        }
    }

    /// <summary>
    /// Verifies raw mode preserves the caller's byte array instance.
    /// </summary>
    [TestMethod]
    public void NoneReturnsOriginalBytes()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'n'];

        byte[] decoded = SearchEncoding.Decode(bytes, SearchEncodingKind.None);

        Assert.AreSame(bytes, decoded);
    }

    private static List<(string Label, string EncodingName)> ReadEncodingRsLabelCases(string text)
    {
        const string LabelToken = "Encoding::for_label(b\"";
        const string SomeToken = "Some(";

        var cases = new List<(string Label, string EncodingName)>();
        int searchIndex = 0;
        while (true)
        {
            int labelStart = text.IndexOf(LabelToken, searchIndex, StringComparison.Ordinal);
            if (labelStart < 0)
            {
                return cases;
            }

            int labelValueStart = labelStart + LabelToken.Length;
            int labelValueEnd = text.IndexOf("\")", labelValueStart, StringComparison.Ordinal);
            if (labelValueEnd < 0)
            {
                throw new InvalidOperationException("Malformed encoding_rs label assertion.");
            }

            int someStart = text.IndexOf(SomeToken, labelValueEnd, StringComparison.Ordinal);
            if (someStart < 0)
            {
                throw new InvalidOperationException("Malformed encoding_rs label assertion.");
            }

            int encodingNameStart = someStart + SomeToken.Length;
            int encodingNameEnd = text.IndexOf(')', encodingNameStart);
            if (encodingNameEnd < 0)
            {
                throw new InvalidOperationException("Malformed encoding_rs label assertion.");
            }

            string label = text[labelValueStart..labelValueEnd];
            string encodingName = text[encodingNameStart..encodingNameEnd].Trim();
            cases.Add((label, encodingName));
            searchIndex = encodingNameEnd + 1;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }

    private static SearchEncodingKind ToSearchEncodingKind(string label, string upstreamEncodingName)
    {
        return upstreamEncodingName switch
        {
            "BIG5" => SearchEncodingKind.Big5,
            "EUC_JP" => SearchEncodingKind.EucJp,
            "EUC_KR" => SearchEncodingKind.EucKr,
            "GB18030" => SearchEncodingKind.Gb18030,
            "GBK" => SearchEncodingKind.Gbk,
            "IBM866" => SearchEncodingKind.Ibm866,
            "ISO_2022_JP" => SearchEncodingKind.Iso2022Jp,
            "ISO_8859_2" => SearchEncodingKind.Iso88592,
            "ISO_8859_3" => SearchEncodingKind.Iso88593,
            "ISO_8859_4" => SearchEncodingKind.Iso88594,
            "ISO_8859_5" => SearchEncodingKind.Iso88595,
            "ISO_8859_6" => SearchEncodingKind.Iso88596,
            "ISO_8859_7" => SearchEncodingKind.Iso88597,
            "ISO_8859_8" => SearchEncodingKind.Iso88598,
            "ISO_8859_8_I" => SearchEncodingKind.Iso88598I,
            "ISO_8859_10" => SearchEncodingKind.Iso885910,
            "ISO_8859_13" => SearchEncodingKind.Iso885913,
            "ISO_8859_14" => SearchEncodingKind.Iso885914,
            "ISO_8859_15" => SearchEncodingKind.Iso885915,
            "ISO_8859_16" => SearchEncodingKind.Iso885916,
            "KOI8_R" => SearchEncodingKind.Koi8R,
            "KOI8_U" => SearchEncodingKind.Koi8U,
            "MACINTOSH" => SearchEncodingKind.Macintosh,
            "SHIFT_JIS" => SearchEncodingKind.ShiftJis,
            "UTF_16BE" => SearchEncodingKind.Utf16Be,
            "UTF_16LE" => string.Equals(label, "utf-16", StringComparison.Ordinal)
                ? SearchEncodingKind.Utf16
                : SearchEncodingKind.Utf16Le,
            "UTF_8" => SearchEncodingKind.Utf8,
            "WINDOWS_874" => SearchEncodingKind.Windows874,
            "WINDOWS_1250" => SearchEncodingKind.Windows1250,
            "WINDOWS_1251" => SearchEncodingKind.Windows1251,
            "WINDOWS_1252" => SearchEncodingKind.Windows1252,
            "WINDOWS_1253" => SearchEncodingKind.Windows1253,
            "WINDOWS_1254" => SearchEncodingKind.Windows1254,
            "WINDOWS_1255" => SearchEncodingKind.Windows1255,
            "WINDOWS_1256" => SearchEncodingKind.Windows1256,
            "WINDOWS_1257" => SearchEncodingKind.Windows1257,
            "WINDOWS_1258" => SearchEncodingKind.Windows1258,
            "X_MAC_CYRILLIC" => SearchEncodingKind.XMacCyrillic,
            "X_USER_DEFINED" => SearchEncodingKind.XUserDefined,
            _ => throw new InvalidOperationException("Unhandled encoding_rs encoding name: " + upstreamEncodingName),
        };
    }

    /// <summary>
    /// Verifies automatic mode sniffs byte-order marks and otherwise preserves bytes.
    /// </summary>
    [TestMethod]
    public void AutoSniffsBomAndOtherwisePreservesBytes()
    {
        Assert.AreSequenceEqual("needle"u8.ToArray(), SearchEncoding.Decode([0xEF, 0xBB, 0xBF, (byte)'n', (byte)'e', (byte)'e', (byte)'d', (byte)'l', (byte)'e'], SearchEncodingKind.Auto));
        Assert.AreSequenceEqual("needle\n"u8.ToArray(), SearchEncoding.Decode([0xFF, 0xFE, (byte)'n', 0, (byte)'e', 0, (byte)'e', 0, (byte)'d', 0, (byte)'l', 0, (byte)'e', 0, (byte)'\n', 0], SearchEncodingKind.Auto));
        Assert.AreSequenceEqual<byte>([0xff, (byte)'n'], SearchEncoding.Decode([0xff, (byte)'n'], SearchEncodingKind.Auto));
    }

    /// <summary>
    /// Verifies explicit UTF-16 modes transcode little-endian and big-endian input.
    /// </summary>
    [TestMethod]
    public void ExplicitUtf16ModesTranscodeToUtf8()
    {
        Assert.AreSequenceEqual("A\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([(byte)'A', 0, 0x3d, 0xd8, 0x00, 0xde], SearchEncodingKind.Utf16Le));
        Assert.AreSequenceEqual("A\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0, (byte)'A', 0xd8, 0x3d, 0xde, 0x00], SearchEncodingKind.Utf16Be));
    }

    /// <summary>
    /// Verifies malformed UTF-16 input uses replacement characters.
    /// </summary>
    [TestMethod]
    public void Utf16ReplacesMalformedUnits()
    {
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x3d, 0xd8, (byte)'A', 0], SearchEncodingKind.Utf16Le));
        Assert.AreSequenceEqual("A\uFFFD"u8.ToArray(), SearchEncoding.Decode([(byte)'A', 0, 0x00], SearchEncodingKind.Utf16Le));
    }

    /// <summary>
    /// Verifies explicit UTF-8 mode replaces malformed byte sequences.
    /// </summary>
    [TestMethod]
    public void Utf8ReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("a\uFFFDb"u8.ToArray(), SearchEncoding.Decode([(byte)'a', 0xff, (byte)'b'], SearchEncodingKind.Utf8));
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0xe2, (byte)'A'], SearchEncodingKind.Utf8));
        Assert.AreSequenceEqual("\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0xf0, 0x9f, 0x98, 0x80], SearchEncodingKind.Utf8));
    }

    /// <summary>
    /// Verifies ISO-2022-JP bytes decode using the WHATWG state machine.
    /// </summary>
    [TestMethod]
    public void Iso2022JpDecodesEscapedJapaneseRomanAndKatakana()
    {
        Assert.AreSequenceEqual(
            "\u65E5\u672C\u8A9E"u8.ToArray(),
            SearchEncoding.Decode([0x1B, (byte)'$', (byte)'B', 0x46, 0x7C, 0x4B, 0x5C, 0x38, 0x6C, 0x1B, (byte)'(', (byte)'B'], SearchEncodingKind.Iso2022Jp));
        Assert.AreSequenceEqual(
            "\u00A5\u203E\uFF76"u8.ToArray(),
            SearchEncoding.Decode([0x1B, (byte)'(', (byte)'J', (byte)'\\', (byte)'~', 0x1B, (byte)'(', (byte)'I', (byte)'6'], SearchEncodingKind.Iso2022Jp));
    }

    /// <summary>
    /// Verifies ISO-2022-JP malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void Iso2022JpReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFDxA"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'x', (byte)'A'], SearchEncodingKind.Iso2022Jp));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'$', (byte)'B', 0x24], SearchEncodingKind.Iso2022Jp));
        Assert.AreSequenceEqual("\uFFFD$"u8.ToArray(), SearchEncoding.Decode([0x1B, (byte)'$'], SearchEncodingKind.Iso2022Jp));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.Iso2022Jp));
    }

    /// <summary>
    /// Verifies EUC-JP bytes decode using the WHATWG mapping.
    /// </summary>
    [TestMethod]
    public void EucJpDecodesJapaneseJis0212AndHalfWidth()
    {
        Assert.AreSequenceEqual("\u65E5\u672C\u8A9E"u8.ToArray(), SearchEncoding.Decode([0xC6, 0xFC, 0xCB, 0xDC, 0xB8, 0xEC], SearchEncodingKind.EucJp));
        Assert.AreSequenceEqual("\u3042\u30A2\uFF76"u8.ToArray(), SearchEncoding.Decode([0xA4, 0xA2, 0xA5, 0xA2, 0x8E, 0xB6], SearchEncodingKind.EucJp));
        Assert.AreSequenceEqual("\u4E02\u02D8"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0, 0xA1, 0x8F, 0xA2, 0xAF], SearchEncodingKind.EucJp));
    }

    /// <summary>
    /// Verifies EUC-JP malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void EucJpReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0xA4, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x8E, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0, (byte)'0'], SearchEncodingKind.EucJp));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x8F, 0xB0], SearchEncodingKind.EucJp));
    }

    /// <summary>
    /// Verifies Shift_JIS bytes decode using the WHATWG mapping.
    /// </summary>
    [TestMethod]
    public void ShiftJisDecodesJapaneseAndSpecialSingles()
    {
        Assert.AreSequenceEqual("\u65E5\u672C\u8A9E"u8.ToArray(), SearchEncoding.Decode([0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA], SearchEncodingKind.ShiftJis));
        Assert.AreSequenceEqual("\u3042\u30A2\uFF76"u8.ToArray(), SearchEncoding.Decode([0x82, 0xA0, 0x83, 0x41, 0xB6], SearchEncodingKind.ShiftJis));
        Assert.AreSequenceEqual("\u0080"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.ShiftJis));
    }

    /// <summary>
    /// Verifies Shift_JIS malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void ShiftJisReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x82, (byte)'0'], SearchEncodingKind.ShiftJis));
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x82, 0xFD, (byte)'A'], SearchEncodingKind.ShiftJis));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x82], SearchEncodingKind.ShiftJis));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0xFD], SearchEncodingKind.ShiftJis));
    }

    /// <summary>
    /// Verifies Big5 bytes decode using the WHATWG mapping.
    /// </summary>
    [TestMethod]
    public void Big5DecodesBmpAstralAndCombinations()
    {
        Assert.AreSequenceEqual("\u4E2D\u6587"u8.ToArray(), SearchEncoding.Decode([0xA4, 0xA4, 0xA4, 0xE5], SearchEncodingKind.Big5));
        Assert.AreSequenceEqual("\U00027267"u8.ToArray(), SearchEncoding.Decode([0x87, 0x45], SearchEncodingKind.Big5));
        Assert.AreSequenceEqual("\u00CA\u0304"u8.ToArray(), SearchEncoding.Decode([0x88, 0x62], SearchEncodingKind.Big5));
    }

    /// <summary>
    /// Verifies Big5 malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void Big5ReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0x88, (byte)'0'], SearchEncodingKind.Big5));
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x80, (byte)'A'], SearchEncodingKind.Big5));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x88], SearchEncodingKind.Big5));
    }

    /// <summary>
    /// Verifies GBK and GB18030 bytes decode using the WHATWG mapping.
    /// </summary>
    [TestMethod]
    public void Gb18030DecodesGbkAndFourByteRanges()
    {
        Assert.AreSequenceEqual("\u20AC\u4F60\u597D"u8.ToArray(), SearchEncoding.Decode([0x80, 0xC4, 0xE3, 0xBA, 0xC3], SearchEncodingKind.Gbk));
        Assert.AreSequenceEqual("\uD83D\uDE00"u8.ToArray(), SearchEncoding.Decode([0x94, 0x39, 0xFC, 0x36], SearchEncodingKind.Gb18030));
    }

    /// <summary>
    /// Verifies GB18030 malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void Gb18030ReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFD0A"u8.ToArray(), SearchEncoding.Decode([0x81, 0x30, (byte)'A'], SearchEncodingKind.Gb18030));
        Assert.AreSequenceEqual("\uFFFD/"u8.ToArray(), SearchEncoding.Decode([0x81, (byte)'/'], SearchEncodingKind.Gbk));
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0x84, 0x39, 0x81, 0x30, (byte)'A'], SearchEncodingKind.Gb18030));
    }

    /// <summary>
    /// Verifies EUC-KR bytes decode using the WHATWG mapping.
    /// </summary>
    [TestMethod]
    public void EucKrDecodesKsx1001AndCp949Extensions()
    {
        Assert.AreSequenceEqual("\uAC00\uB098\uB2E4"u8.ToArray(), SearchEncoding.Decode([0xB0, 0xA1, 0xB3, 0xAA, 0xB4, 0xD9], SearchEncodingKind.EucKr));
        Assert.AreSequenceEqual("\uAC02"u8.ToArray(), SearchEncoding.Decode([0x81, 0x41], SearchEncodingKind.EucKr));
    }

    /// <summary>
    /// Verifies EUC-KR malformed byte sequences match WHATWG consumption rules.
    /// </summary>
    [TestMethod]
    public void EucKrReplacesMalformedSequences()
    {
        Assert.AreSequenceEqual("\uFFFD0"u8.ToArray(), SearchEncoding.Decode([0xB0, (byte)'0'], SearchEncodingKind.EucKr));
        Assert.AreSequenceEqual("\uFFFDA"u8.ToArray(), SearchEncoding.Decode([0xB0, 0x80, (byte)'A'], SearchEncodingKind.EucKr));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0xB0], SearchEncodingKind.EucKr));
        Assert.AreSequenceEqual("\uFFFD"u8.ToArray(), SearchEncoding.Decode([0x80], SearchEncodingKind.EucKr));
    }

    /// <summary>
    /// Verifies Windows-1252 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1252DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0x82, 0x81, 0xA0, 0xFF], SearchEncodingKind.Windows1252);

        Assert.AreSequenceEqual("\u20AC\u201A\u0081\u00A0\u00FF"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies IBM866 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Ibm866DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x8F, 0xAF, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2], SearchEncodingKind.Ibm866);

        Assert.AreSequenceEqual("\u041F\u043F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-2 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88592DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xC6, 0xE6, 0xFF], SearchEncodingKind.Iso88592);

        Assert.AreSequenceEqual("\u0104\u0105\u0106\u0107\u02D9"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-3 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88593DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA5, 0xB1], SearchEncodingKind.Iso88593);

        Assert.AreSequenceEqual("\u0126\uFFFD\u0127"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-4 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88594DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xC7, 0xE7], SearchEncodingKind.Iso88594);

        Assert.AreSequenceEqual("\u0104\u0105\u012E\u012F"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-5 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88595DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB0, 0xD0, 0xF0], SearchEncodingKind.Iso88595);

        Assert.AreSequenceEqual("\u0401\u0410\u0430\u2116"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-6 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88596DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC7, 0xE3, 0xA1], SearchEncodingKind.Iso88596);

        Assert.AreSequenceEqual("\u0627\u0643\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-7 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88597DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC1, 0xE1, 0xAE], SearchEncodingKind.Iso88597);

        Assert.AreSequenceEqual("\u0391\u03B1\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-8 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88598DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA, 0xA1], SearchEncodingKind.Iso88598);

        Assert.AreSequenceEqual("\u05D0\u05EA\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-8-I bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso88598IDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA], SearchEncodingKind.Iso88598I);

        Assert.AreSequenceEqual("\u05D0\u05EA"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-10 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso885910DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB1, 0xFF], SearchEncodingKind.Iso885910);

        Assert.AreSequenceEqual("\u0104\u0105\u0138"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-13 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso885913DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xB4, 0xFF], SearchEncodingKind.Iso885913);

        Assert.AreSequenceEqual("\u201D\u201C\u2019"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-14 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso885914DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA2, 0xD0, 0xF0], SearchEncodingKind.Iso885914);

        Assert.AreSequenceEqual("\u1E02\u1E03\u0174\u0175"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-15 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso885915DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA4, 0xBC, 0xBE], SearchEncodingKind.Iso885915);

        Assert.AreSequenceEqual("\u20AC\u0152\u0178"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies ISO-8859-16 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Iso885916DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xA2, 0xAA, 0xFE], SearchEncodingKind.Iso885916);

        Assert.AreSequenceEqual("\u0104\u0105\u0218\u021B"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies KOI8-R bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Koi8RDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xF0, 0xD2, 0xC9, 0xD7, 0xC5, 0xD4], SearchEncodingKind.Koi8R);

        Assert.AreSequenceEqual("\u041F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies KOI8-U bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Koi8UDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA4, 0xB4], SearchEncodingKind.Koi8U);

        Assert.AreSequenceEqual("\u0454\u0404"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Macintosh bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void MacintoshDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0x8E, 0xDB], SearchEncodingKind.Macintosh);

        Assert.AreSequenceEqual("\u00C4\u00E9\u20AC"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-874 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows874DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xA1, 0xDF, 0xFB], SearchEncodingKind.Windows874);

        Assert.AreSequenceEqual("\u0E01\u0E3F\u0E5B"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1250 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1250DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x8C, 0x9C, 0xA5, 0xB9], SearchEncodingKind.Windows1250);

        Assert.AreSequenceEqual("\u015A\u015B\u0104\u0105"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1251 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1251DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x88, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2], SearchEncodingKind.Windows1251);

        Assert.AreSequenceEqual("\u20AC\u041F\u0440\u0438\u0432\u0435\u0442"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1253 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1253DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xAA, 0xC1, 0xE1], SearchEncodingKind.Windows1253);

        Assert.AreSequenceEqual("\uFFFD\u0391\u03B1"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1254 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1254DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xD0, 0xDD, 0xFD, 0xFE], SearchEncodingKind.Windows1254);

        Assert.AreSequenceEqual("\u011E\u0130\u0131\u015F"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1255 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1255DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xE0, 0xFA, 0xC0], SearchEncodingKind.Windows1255);

        Assert.AreSequenceEqual("\u05D0\u05EA\u05B0"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1256 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1256DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC7, 0xE3, 0xED], SearchEncodingKind.Windows1256);

        Assert.AreSequenceEqual("\u0627\u0645\u064A"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1257 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1257DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xC0, 0xE0, 0xA1], SearchEncodingKind.Windows1257);

        Assert.AreSequenceEqual("\u0104\u0105\uFFFD"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies Windows-1258 bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void Windows1258DecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0xCC, 0xD2, 0xF2, 0xFE], SearchEncodingKind.Windows1258);

        Assert.AreSequenceEqual("\u0300\u0309\u0323\u20AB"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies x-mac-cyrillic bytes decode using the WHATWG single-byte mapping.
    /// </summary>
    [TestMethod]
    public void XMacCyrillicDecodesSingleByteTable()
    {
        byte[] decoded = SearchEncoding.Decode([0x80, 0xDF, 0xFF], SearchEncodingKind.XMacCyrillic);

        Assert.AreSequenceEqual("\u0410\u044F\u20AC"u8.ToArray(), decoded);
    }

    /// <summary>
    /// Verifies x-user-defined bytes decode using the WHATWG private-use mapping.
    /// </summary>
    [TestMethod]
    public void XUserDefinedDecodesPrivateUseMapping()
    {
        byte[] decoded = SearchEncoding.Decode([(byte)'A', 0x80, 0xFF], SearchEncodingKind.XUserDefined);

        Assert.AreSequenceEqual("A\uF780\uF7FF"u8.ToArray(), decoded);
    }
}
