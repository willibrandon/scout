namespace Scout;

/// <summary>
/// Records the variables encountered while parsing a hyperlink format.
/// </summary>
[Flags]
internal enum HyperlinkVariables
{
    None = 0,
    Any = 1,
    Path = 2,
    Line = 4,
    Column = 8,
}
