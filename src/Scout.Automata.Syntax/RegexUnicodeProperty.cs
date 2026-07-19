namespace Scout;

/// <summary>
/// Describes one resolved Unicode property without constraining its generated identifier to one byte.
/// </summary>
internal sealed class RegexUnicodeProperty(
    RegexUnicodePropertyFamily family,
    RegexUnicodePropertyKind propertyKind,
    RegexUnicodeScriptKind scriptKind)
{
    internal RegexUnicodePropertyFamily Family { get; } = family;

    internal RegexUnicodePropertyKind PropertyKind { get; } = propertyKind;

    internal RegexUnicodeScriptKind ScriptKind { get; } = scriptKind;
}
