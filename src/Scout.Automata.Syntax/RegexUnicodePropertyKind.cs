namespace Scout;

/// <summary>
/// Identifies a pinned Unicode property table used by regex property classes.
/// </summary>
public enum RegexUnicodePropertyKind
{
    /// <summary>
    /// No Unicode property table.
    /// </summary>
    None,

    /// <summary>
    /// The Cased_Letter general category.
    /// </summary>
    CasedLetter,

    /// <summary>
    /// The Close_Punctuation general category.
    /// </summary>
    ClosePunctuation,

    /// <summary>
    /// The Connector_Punctuation general category.
    /// </summary>
    ConnectorPunctuation,

    /// <summary>
    /// The Control general category.
    /// </summary>
    Control,

    /// <summary>
    /// The Currency_Symbol general category.
    /// </summary>
    CurrencySymbol,

    /// <summary>
    /// The Dash_Punctuation general category.
    /// </summary>
    DashPunctuation,

    /// <summary>
    /// The Decimal_Number general category.
    /// </summary>
    DecimalNumber,

    /// <summary>
    /// The Enclosing_Mark general category.
    /// </summary>
    EnclosingMark,

    /// <summary>
    /// The Final_Punctuation general category.
    /// </summary>
    FinalPunctuation,

    /// <summary>
    /// The Format general category.
    /// </summary>
    Format,

    /// <summary>
    /// The Initial_Punctuation general category.
    /// </summary>
    InitialPunctuation,

    /// <summary>
    /// The Letter general category.
    /// </summary>
    Letter,

    /// <summary>
    /// The Letter_Number general category.
    /// </summary>
    LetterNumber,

    /// <summary>
    /// The Line_Separator general category.
    /// </summary>
    LineSeparator,

    /// <summary>
    /// The Lowercase_Letter general category.
    /// </summary>
    LowercaseLetter,

    /// <summary>
    /// The Mark general category.
    /// </summary>
    Mark,

    /// <summary>
    /// The Math binary property.
    /// </summary>
    Math,

    /// <summary>
    /// The Math_Symbol general category.
    /// </summary>
    MathSymbol,

    /// <summary>
    /// The Modifier_Letter general category.
    /// </summary>
    ModifierLetter,

    /// <summary>
    /// The Modifier_Symbol general category.
    /// </summary>
    ModifierSymbol,

    /// <summary>
    /// The Nonspacing_Mark general category.
    /// </summary>
    NonspacingMark,

    /// <summary>
    /// The Emoji binary property.
    /// </summary>
    Emoji,

    /// <summary>
    /// The Extended_Pictographic binary property.
    /// </summary>
    ExtendedPictographic,

    /// <summary>
    /// The Script=Cyrillic property value.
    /// </summary>
    ScriptCyrillic,

    /// <summary>
    /// The Script_Extensions=Cyrillic property value.
    /// </summary>
    ScriptExtensionCyrillic,

    /// <summary>
    /// The Grapheme_Cluster_Break=Prepend property value.
    /// </summary>
    GraphemeClusterBreakPrepend,

    /// <summary>
    /// The Grapheme_Cluster_Break=Regional_Indicator property value.
    /// </summary>
    GraphemeClusterBreakRegionalIndicator,

    /// <summary>
    /// The Grapheme_Cluster_Break=LVT property value.
    /// </summary>
    GraphemeClusterBreakLvt,

    /// <summary>
    /// The Grapheme_Cluster_Break=ZWJ property value.
    /// </summary>
    GraphemeClusterBreakZwj,

    /// <summary>
    /// The Word_Break=Hebrew_Letter property value.
    /// </summary>
    WordBreakHebrewLetter,

    /// <summary>
    /// The Word_Break=ExtendNumLet property value.
    /// </summary>
    WordBreakExtendNumLet,

    /// <summary>
    /// The Word_Break=WSegSpace property value.
    /// </summary>
    WordBreakWSegSpace,

    /// <summary>
    /// The Word_Break=Numeric property value.
    /// </summary>
    WordBreakNumeric,

    /// <summary>
    /// The Sentence_Break=Lower property value.
    /// </summary>
    SentenceBreakLower,

    /// <summary>
    /// The Sentence_Break=Close property value.
    /// </summary>
    SentenceBreakClose,

    /// <summary>
    /// The Sentence_Break=SContinue property value.
    /// </summary>
    SentenceBreakSContinue,

    /// <summary>
    /// The Number general category.
    /// </summary>
    Number,

    /// <summary>
    /// The Open_Punctuation general category.
    /// </summary>
    OpenPunctuation,

    /// <summary>
    /// The Other general category.
    /// </summary>
    Other,

    /// <summary>
    /// The Other_Letter general category.
    /// </summary>
    OtherLetter,

    /// <summary>
    /// The Other_Number general category.
    /// </summary>
    OtherNumber,

    /// <summary>
    /// The Other_Punctuation general category.
    /// </summary>
    OtherPunctuation,

    /// <summary>
    /// The Other_Symbol general category.
    /// </summary>
    OtherSymbol,

    /// <summary>
    /// The Paragraph_Separator general category.
    /// </summary>
    ParagraphSeparator,

    /// <summary>
    /// The Private_Use general category.
    /// </summary>
    PrivateUse,

    /// <summary>
    /// The Punctuation general category.
    /// </summary>
    Punctuation,

    /// <summary>
    /// The Separator general category.
    /// </summary>
    Separator,

    /// <summary>
    /// The Space_Separator general category.
    /// </summary>
    SpaceSeparator,

    /// <summary>
    /// The Spacing_Mark general category.
    /// </summary>
    SpacingMark,

    /// <summary>
    /// The Symbol general category.
    /// </summary>
    Symbol,

    /// <summary>
    /// The Titlecase_Letter general category.
    /// </summary>
    TitlecaseLetter,

    /// <summary>
    /// The Unassigned general category.
    /// </summary>
    Unassigned,

    /// <summary>
    /// The Uppercase_Letter general category.
    /// </summary>
    UppercaseLetter,
}
