
namespace Scout;

/// <summary>
/// Configures PCRE2 pattern compilation.
/// </summary>
[Flags]
public enum Pcre2CompileOptions
{
    /// <summary>
    /// Uses PCRE2 default compilation behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Enables case-insensitive matching.
    /// </summary>
    CaseInsensitive = 0x00000008,

    /// <summary>
    /// Makes dot match line terminators.
    /// </summary>
    DotMatchesNewline = 0x00000020,

    /// <summary>
    /// Makes anchors use multiline semantics.
    /// </summary>
    MultiLine = 0x00000400,

    /// <summary>
    /// Enables Unicode character properties.
    /// </summary>
    UnicodeProperties = 0x00020000,

    /// <summary>
    /// Enables UTF-8 pattern and subject handling.
    /// </summary>
    Utf = 0x00080000,

    /// <summary>
    /// Allows matching UTF-8 mode patterns against subjects that contain invalid UTF-8 bytes.
    /// </summary>
    MatchInvalidUtf = 0x04000000
}
