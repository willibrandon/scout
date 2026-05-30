namespace Scout;

internal enum Iso2022JpState
{
    Ascii,
    Roman,
    Katakana,
    LeadByte,
    TrailByte,
    EscapeStart,
    Escape,
}
