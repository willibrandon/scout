namespace Scout;

/// <summary>
/// Identifies the requested input encoding mode.
/// </summary>
public enum CliEncodingMode
{
    /// <summary>
    /// Sniff byte-order marks and otherwise search raw bytes.
    /// </summary>
    Auto,

    /// <summary>
    /// Disable byte-order mark sniffing and search raw bytes.
    /// </summary>
    None,

    /// <summary>
    /// Decode as UTF-8 after byte-order mark sniffing.
    /// </summary>
    Utf8,

    /// <summary>
    /// Decode as UTF-16 little-endian after byte-order mark sniffing.
    /// </summary>
    Utf16,

    /// <summary>
    /// Decode as UTF-16 little-endian after byte-order mark sniffing.
    /// </summary>
    Utf16Le,

    /// <summary>
    /// Decode as UTF-16 big-endian after byte-order mark sniffing.
    /// </summary>
    Utf16Be,

    /// <summary>
    /// Decode as EUC-KR after byte-order mark sniffing.
    /// </summary>
    EucKr,

    /// <summary>
    /// Decode as EUC-JP after byte-order mark sniffing.
    /// </summary>
    EucJp,

    /// <summary>
    /// Decode as Big5 after byte-order mark sniffing.
    /// </summary>
    Big5,

    /// <summary>
    /// Decode as GB18030 after byte-order mark sniffing.
    /// </summary>
    Gb18030,

    /// <summary>
    /// Decode as GBK after byte-order mark sniffing.
    /// </summary>
    Gbk,

    /// <summary>
    /// Decode as Shift_JIS after byte-order mark sniffing.
    /// </summary>
    ShiftJis,

    /// <summary>
    /// Decode as IBM866 after byte-order mark sniffing.
    /// </summary>
    Ibm866,

    /// <summary>
    /// Decode as ISO-8859-2 after byte-order mark sniffing.
    /// </summary>
    Iso88592,

    /// <summary>
    /// Decode as ISO-8859-3 after byte-order mark sniffing.
    /// </summary>
    Iso88593,

    /// <summary>
    /// Decode as ISO-8859-4 after byte-order mark sniffing.
    /// </summary>
    Iso88594,

    /// <summary>
    /// Decode as ISO-8859-5 after byte-order mark sniffing.
    /// </summary>
    Iso88595,

    /// <summary>
    /// Decode as ISO-8859-6 after byte-order mark sniffing.
    /// </summary>
    Iso88596,

    /// <summary>
    /// Decode as ISO-8859-7 after byte-order mark sniffing.
    /// </summary>
    Iso88597,

    /// <summary>
    /// Decode as ISO-8859-8 after byte-order mark sniffing.
    /// </summary>
    Iso88598,

    /// <summary>
    /// Decode as ISO-8859-8-I after byte-order mark sniffing.
    /// </summary>
    Iso88598I,

    /// <summary>
    /// Decode as ISO-8859-10 after byte-order mark sniffing.
    /// </summary>
    Iso885910,

    /// <summary>
    /// Decode as ISO-8859-13 after byte-order mark sniffing.
    /// </summary>
    Iso885913,

    /// <summary>
    /// Decode as ISO-8859-14 after byte-order mark sniffing.
    /// </summary>
    Iso885914,

    /// <summary>
    /// Decode as ISO-8859-15 after byte-order mark sniffing.
    /// </summary>
    Iso885915,

    /// <summary>
    /// Decode as ISO-8859-16 after byte-order mark sniffing.
    /// </summary>
    Iso885916,

    /// <summary>
    /// Decode as ISO-2022-JP after byte-order mark sniffing.
    /// </summary>
    Iso2022Jp,

    /// <summary>
    /// Decode as KOI8-R after byte-order mark sniffing.
    /// </summary>
    Koi8R,

    /// <summary>
    /// Decode as KOI8-U after byte-order mark sniffing.
    /// </summary>
    Koi8U,

    /// <summary>
    /// Decode as Macintosh after byte-order mark sniffing.
    /// </summary>
    Macintosh,

    /// <summary>
    /// Decode as Windows-874 after byte-order mark sniffing.
    /// </summary>
    Windows874,

    /// <summary>
    /// Decode as Windows-1250 after byte-order mark sniffing.
    /// </summary>
    Windows1250,

    /// <summary>
    /// Decode as Windows-1251 after byte-order mark sniffing.
    /// </summary>
    Windows1251,

    /// <summary>
    /// Decode as Windows-1252 after byte-order mark sniffing.
    /// </summary>
    Windows1252,

    /// <summary>
    /// Decode as Windows-1253 after byte-order mark sniffing.
    /// </summary>
    Windows1253,

    /// <summary>
    /// Decode as Windows-1254 after byte-order mark sniffing.
    /// </summary>
    Windows1254,

    /// <summary>
    /// Decode as Windows-1255 after byte-order mark sniffing.
    /// </summary>
    Windows1255,

    /// <summary>
    /// Decode as Windows-1256 after byte-order mark sniffing.
    /// </summary>
    Windows1256,

    /// <summary>
    /// Decode as Windows-1257 after byte-order mark sniffing.
    /// </summary>
    Windows1257,

    /// <summary>
    /// Decode as Windows-1258 after byte-order mark sniffing.
    /// </summary>
    Windows1258,

    /// <summary>
    /// Decode as x-mac-cyrillic after byte-order mark sniffing.
    /// </summary>
    XMacCyrillic,

    /// <summary>
    /// Decode as x-user-defined after byte-order mark sniffing.
    /// </summary>
    XUserDefined,
}
