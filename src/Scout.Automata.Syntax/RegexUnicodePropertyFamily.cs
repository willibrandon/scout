namespace Scout;

/// <summary>
/// Identifies the generated Unicode table family used by a property class.
/// </summary>
internal enum RegexUnicodePropertyFamily
{
    /// <summary>
    /// Identifies a general category, Boolean, or break property.
    /// </summary>
    Property,

    /// <summary>
    /// Identifies a Unicode Script property.
    /// </summary>
    Script,

    /// <summary>
    /// Identifies a Unicode Script_Extensions property.
    /// </summary>
    ScriptExtensions,
}
