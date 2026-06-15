
namespace Scout;

/// <summary>
/// Resolves pinned regex-syntax Unicode property names to Scout property tables.
/// </summary>
public static class RegexUnicodePropertyNames
{
    /// <summary>
    /// Determines whether a Unicode property name identifies a pinned Scout property table.
    /// </summary>
    /// <param name="name">The UTF-8 property name bytes from the regex pattern.</param>
    /// <param name="propertyKind">The resolved property table kind.</param>
    /// <returns><see langword="true" /> when <paramref name="name" /> is supported.</returns>
    public static bool TryGetKind(ReadOnlySpan<byte> name, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.Letter;
        if (TryGetBreakPropertyKind(name, out propertyKind))
        {
            return true;
        }

        if (TryGetNamedScriptPropertyKind(name, out propertyKind))
        {
            return true;
        }

        if (TryGetNamedGeneralCategoryPropertyKind(name, out propertyKind))
        {
            return true;
        }

        if (NameEquals(name, "lc") || NameEquals(name, "casedletter"))
        {
            propertyKind = RegexUnicodePropertyKind.CasedLetter;
            return true;
        }

        if (NameEquals(name, "pe") || NameEquals(name, "closepunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ClosePunctuation;
            return true;
        }

        if (NameEquals(name, "pc") || NameEquals(name, "connectorpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ConnectorPunctuation;
            return true;
        }

        if (NameEquals(name, "cc") || NameEquals(name, "control"))
        {
            propertyKind = RegexUnicodePropertyKind.Control;
            return true;
        }

        if (NameEquals(name, "sc") || NameEquals(name, "currencysymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.CurrencySymbol;
            return true;
        }

        if (NameEquals(name, "pd") || NameEquals(name, "dashpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.DashPunctuation;
            return true;
        }

        if (NameEquals(name, "nd") || NameEquals(name, "decimalnumber") || NameEquals(name, "digit"))
        {
            propertyKind = RegexUnicodePropertyKind.DecimalNumber;
            return true;
        }

        if (NameEquals(name, "me") || NameEquals(name, "enclosingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.EnclosingMark;
            return true;
        }

        if (NameEquals(name, "pf") || NameEquals(name, "finalpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.FinalPunctuation;
            return true;
        }

        if (NameEquals(name, "cf") || NameEquals(name, "format"))
        {
            propertyKind = RegexUnicodePropertyKind.Format;
            return true;
        }

        if (NameEquals(name, "pi") || NameEquals(name, "initialpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.InitialPunctuation;
            return true;
        }

        if (NameEquals(name, "l") || NameEquals(name, "letter"))
        {
            propertyKind = RegexUnicodePropertyKind.Letter;
            return true;
        }

        if (NameEquals(name, "nl") || NameEquals(name, "letternumber"))
        {
            propertyKind = RegexUnicodePropertyKind.LetterNumber;
            return true;
        }

        if (NameEquals(name, "zl") || NameEquals(name, "lineseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.LineSeparator;
            return true;
        }

        if (NameEquals(name, "ll") || NameEquals(name, "lowercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.LowercaseLetter;
            return true;
        }

        if (NameEquals(name, "lowercase"))
        {
            propertyKind = RegexUnicodePropertyKind.Lowercase;
            return true;
        }

        if (NameEquals(name, "m") || NameEquals(name, "mark"))
        {
            propertyKind = RegexUnicodePropertyKind.Mark;
            return true;
        }

        if (NameEquals(name, "math"))
        {
            propertyKind = RegexUnicodePropertyKind.Math;
            return true;
        }

        if (NameEquals(name, "sm") || NameEquals(name, "mathsymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.MathSymbol;
            return true;
        }

        if (NameEquals(name, "lm") || NameEquals(name, "modifierletter"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierLetter;
            return true;
        }

        if (NameEquals(name, "sk") || NameEquals(name, "modifiersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierSymbol;
            return true;
        }

        if (NameEquals(name, "mn") || NameEquals(name, "nonspacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.NonspacingMark;
            return true;
        }

        if (NameEquals(name, "emoji"))
        {
            propertyKind = RegexUnicodePropertyKind.Emoji;
            return true;
        }

        if (NameEquals(name, "extendedpictographic"))
        {
            propertyKind = RegexUnicodePropertyKind.ExtendedPictographic;
            return true;
        }

        if (NameEquals(name, "n") || NameEquals(name, "number"))
        {
            propertyKind = RegexUnicodePropertyKind.Number;
            return true;
        }

        if (NameEquals(name, "ps") || NameEquals(name, "openpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OpenPunctuation;
            return true;
        }

        if (NameEquals(name, "c") || NameEquals(name, "other"))
        {
            propertyKind = RegexUnicodePropertyKind.Other;
            return true;
        }

        if (NameEquals(name, "lo") || NameEquals(name, "otherletter"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherLetter;
            return true;
        }

        if (NameEquals(name, "no") || NameEquals(name, "othernumber"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherNumber;
            return true;
        }

        if (NameEquals(name, "po") || NameEquals(name, "otherpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherPunctuation;
            return true;
        }

        if (NameEquals(name, "so") || NameEquals(name, "othersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherSymbol;
            return true;
        }

        if (NameEquals(name, "zp") || NameEquals(name, "paragraphseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.ParagraphSeparator;
            return true;
        }

        if (NameEquals(name, "co") || NameEquals(name, "privateuse"))
        {
            propertyKind = RegexUnicodePropertyKind.PrivateUse;
            return true;
        }

        if (NameEquals(name, "p") || NameEquals(name, "punctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.Punctuation;
            return true;
        }

        if (NameEquals(name, "z") || NameEquals(name, "separator"))
        {
            propertyKind = RegexUnicodePropertyKind.Separator;
            return true;
        }

        if (NameEquals(name, "zs") || NameEquals(name, "spaceseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.SpaceSeparator;
            return true;
        }

        if (NameEquals(name, "mc") || NameEquals(name, "spacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.SpacingMark;
            return true;
        }

        if (NameEquals(name, "s") || NameEquals(name, "symbol"))
        {
            propertyKind = RegexUnicodePropertyKind.Symbol;
            return true;
        }

        if (NameEquals(name, "lt") || NameEquals(name, "titlecaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.TitlecaseLetter;
            return true;
        }

        if (NameEquals(name, "cn") || NameEquals(name, "unassigned"))
        {
            propertyKind = RegexUnicodePropertyKind.Unassigned;
            return true;
        }

        if (NameEquals(name, "lu") || NameEquals(name, "uppercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.UppercaseLetter;
            return true;
        }

        if (NameEquals(name, "uppercase"))
        {
            propertyKind = RegexUnicodePropertyKind.Uppercase;
            return true;
        }

        return TryGetBareScriptKind(name, out propertyKind);
    }

    private static bool TryGetNamedGeneralCategoryPropertyKind(ReadOnlySpan<byte> name, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        int separator = FindPropertySeparator(name);
        if (separator < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> propertyName = name[..separator];
        ReadOnlySpan<byte> propertyValue = name[(separator + 1)..];
        return (NameEquals(propertyName, "generalcategory") || NameEquals(propertyName, "gc")) &&
            TryGetGeneralCategoryKind(propertyValue, out propertyKind);
    }

    private static bool TryGetGeneralCategoryKind(ReadOnlySpan<byte> value, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (NameEquals(value, "lc") || NameEquals(value, "casedletter"))
        {
            propertyKind = RegexUnicodePropertyKind.CasedLetter;
            return true;
        }

        if (NameEquals(value, "pe") || NameEquals(value, "closepunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ClosePunctuation;
            return true;
        }

        if (NameEquals(value, "pc") || NameEquals(value, "connectorpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.ConnectorPunctuation;
            return true;
        }

        if (NameEquals(value, "cc") || NameEquals(value, "control"))
        {
            propertyKind = RegexUnicodePropertyKind.Control;
            return true;
        }

        if (NameEquals(value, "sc") || NameEquals(value, "currencysymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.CurrencySymbol;
            return true;
        }

        if (NameEquals(value, "pd") || NameEquals(value, "dashpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.DashPunctuation;
            return true;
        }

        if (NameEquals(value, "nd") || NameEquals(value, "decimalnumber") || NameEquals(value, "digit"))
        {
            propertyKind = RegexUnicodePropertyKind.DecimalNumber;
            return true;
        }

        if (NameEquals(value, "me") || NameEquals(value, "enclosingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.EnclosingMark;
            return true;
        }

        if (NameEquals(value, "pf") || NameEquals(value, "finalpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.FinalPunctuation;
            return true;
        }

        if (NameEquals(value, "cf") || NameEquals(value, "format"))
        {
            propertyKind = RegexUnicodePropertyKind.Format;
            return true;
        }

        if (NameEquals(value, "pi") || NameEquals(value, "initialpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.InitialPunctuation;
            return true;
        }

        if (NameEquals(value, "l") || NameEquals(value, "letter"))
        {
            propertyKind = RegexUnicodePropertyKind.Letter;
            return true;
        }

        if (NameEquals(value, "nl") || NameEquals(value, "letternumber"))
        {
            propertyKind = RegexUnicodePropertyKind.LetterNumber;
            return true;
        }

        if (NameEquals(value, "zl") || NameEquals(value, "lineseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.LineSeparator;
            return true;
        }

        if (NameEquals(value, "ll") || NameEquals(value, "lowercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.LowercaseLetter;
            return true;
        }

        if (NameEquals(value, "m") || NameEquals(value, "mark"))
        {
            propertyKind = RegexUnicodePropertyKind.Mark;
            return true;
        }

        if (NameEquals(value, "sm") || NameEquals(value, "mathsymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.MathSymbol;
            return true;
        }

        if (NameEquals(value, "lm") || NameEquals(value, "modifierletter"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierLetter;
            return true;
        }

        if (NameEquals(value, "sk") || NameEquals(value, "modifiersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.ModifierSymbol;
            return true;
        }

        if (NameEquals(value, "mn") || NameEquals(value, "nonspacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.NonspacingMark;
            return true;
        }

        if (NameEquals(value, "n") || NameEquals(value, "number"))
        {
            propertyKind = RegexUnicodePropertyKind.Number;
            return true;
        }

        if (NameEquals(value, "ps") || NameEquals(value, "openpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OpenPunctuation;
            return true;
        }

        if (NameEquals(value, "c") || NameEquals(value, "other"))
        {
            propertyKind = RegexUnicodePropertyKind.Other;
            return true;
        }

        if (NameEquals(value, "lo") || NameEquals(value, "otherletter"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherLetter;
            return true;
        }

        if (NameEquals(value, "no") || NameEquals(value, "othernumber"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherNumber;
            return true;
        }

        if (NameEquals(value, "po") || NameEquals(value, "otherpunctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherPunctuation;
            return true;
        }

        if (NameEquals(value, "so") || NameEquals(value, "othersymbol"))
        {
            propertyKind = RegexUnicodePropertyKind.OtherSymbol;
            return true;
        }

        if (NameEquals(value, "zp") || NameEquals(value, "paragraphseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.ParagraphSeparator;
            return true;
        }

        if (NameEquals(value, "co") || NameEquals(value, "privateuse"))
        {
            propertyKind = RegexUnicodePropertyKind.PrivateUse;
            return true;
        }

        if (NameEquals(value, "p") || NameEquals(value, "punctuation"))
        {
            propertyKind = RegexUnicodePropertyKind.Punctuation;
            return true;
        }

        if (NameEquals(value, "z") || NameEquals(value, "separator"))
        {
            propertyKind = RegexUnicodePropertyKind.Separator;
            return true;
        }

        if (NameEquals(value, "zs") || NameEquals(value, "spaceseparator"))
        {
            propertyKind = RegexUnicodePropertyKind.SpaceSeparator;
            return true;
        }

        if (NameEquals(value, "mc") || NameEquals(value, "spacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.SpacingMark;
            return true;
        }

        if (NameEquals(value, "s") || NameEquals(value, "symbol"))
        {
            propertyKind = RegexUnicodePropertyKind.Symbol;
            return true;
        }

        if (NameEquals(value, "lt") || NameEquals(value, "titlecaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.TitlecaseLetter;
            return true;
        }

        if (NameEquals(value, "cn") || NameEquals(value, "unassigned"))
        {
            propertyKind = RegexUnicodePropertyKind.Unassigned;
            return true;
        }

        if (NameEquals(value, "lu") || NameEquals(value, "uppercaseletter"))
        {
            propertyKind = RegexUnicodePropertyKind.UppercaseLetter;
            return true;
        }

        return false;
    }

    private static bool TryGetNamedScriptPropertyKind(ReadOnlySpan<byte> name, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        int separator = FindPropertySeparator(name);
        if (separator < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> propertyName = name[..separator];
        ReadOnlySpan<byte> propertyValue = name[(separator + 1)..];
        if ((NameEquals(propertyName, "script") || NameEquals(propertyName, "sc")) &&
            TryGetBareScriptKind(propertyValue, out propertyKind))
        {
            return true;
        }

        return (NameEquals(propertyName, "scriptextensions") || NameEquals(propertyName, "scx")) &&
            TryGetScriptExtensionKind(propertyValue, out propertyKind);
    }

    private static bool TryGetBareScriptKind(ReadOnlySpan<byte> value, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (NameEquals(value, "cyrillic") || NameEquals(value, "cyrl"))
        {
            propertyKind = RegexUnicodePropertyKind.ScriptCyrillic;
            return true;
        }

        if (NameEquals(value, "greek") || NameEquals(value, "grek"))
        {
            propertyKind = RegexUnicodePropertyKind.ScriptGreek;
            return true;
        }

        return false;
    }

    private static bool TryGetScriptExtensionKind(ReadOnlySpan<byte> value, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (NameEquals(value, "cyrillic") || NameEquals(value, "cyrl"))
        {
            propertyKind = RegexUnicodePropertyKind.ScriptExtensionCyrillic;
            return true;
        }

        if (NameEquals(value, "greek") || NameEquals(value, "grek"))
        {
            propertyKind = RegexUnicodePropertyKind.ScriptExtensionGreek;
            return true;
        }

        return false;
    }

    private static bool TryGetBreakPropertyKind(ReadOnlySpan<byte> name, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        int separator = FindPropertySeparator(name);
        if (separator >= 0)
        {
            ReadOnlySpan<byte> propertyName = name[..separator];
            ReadOnlySpan<byte> propertyValue = name[(separator + 1)..];
            if ((NameEquals(propertyName, "graphemeclusterbreak") || NameEquals(propertyName, "gcb")) &&
                TryGetGraphemeClusterBreakKind(propertyValue, allowAmbiguousNames: true, out propertyKind))
            {
                return true;
            }

            if ((NameEquals(propertyName, "wordbreak") || NameEquals(propertyName, "wb")) &&
                TryGetWordBreakKind(propertyValue, out propertyKind))
            {
                return true;
            }

            return (NameEquals(propertyName, "sentencebreak") || NameEquals(propertyName, "sb")) &&
                TryGetSentenceBreakKind(propertyValue, out propertyKind);
        }

        return TryGetGraphemeClusterBreakKind(name, allowAmbiguousNames: false, out propertyKind);
    }

    private static int FindPropertySeparator(ReadOnlySpan<byte> name)
    {
        for (int index = 0; index < name.Length; index++)
        {
            if (name[index] is (byte)'=' or (byte)':')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetGraphemeClusterBreakKind(
        ReadOnlySpan<byte> value,
        bool allowAmbiguousNames,
        out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (allowAmbiguousNames && NameEquals(value, "cr"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakCr;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "lf"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakLf;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "control"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakControl;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "extend"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakExtend;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "l"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakL;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "lv"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakLv;
            return true;
        }

        if (NameEquals(value, "prepend"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakPrepend;
            return true;
        }

        if (NameEquals(value, "regionalindicator") || NameEquals(value, "ri"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakRegionalIndicator;
            return true;
        }

        if (NameEquals(value, "lvt"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakLvt;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "spacingmark"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakSpacingMark;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "t"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakT;
            return true;
        }

        if (allowAmbiguousNames && NameEquals(value, "v"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakV;
            return true;
        }

        if (NameEquals(value, "zwj"))
        {
            propertyKind = RegexUnicodePropertyKind.GraphemeClusterBreakZwj;
            return true;
        }

        return false;
    }

    private static bool TryGetWordBreakKind(ReadOnlySpan<byte> value, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (NameEquals(value, "hebrewletter"))
        {
            propertyKind = RegexUnicodePropertyKind.WordBreakHebrewLetter;
            return true;
        }

        if (NameEquals(value, "extendnumlet"))
        {
            propertyKind = RegexUnicodePropertyKind.WordBreakExtendNumLet;
            return true;
        }

        if (NameEquals(value, "wsegspace"))
        {
            propertyKind = RegexUnicodePropertyKind.WordBreakWSegSpace;
            return true;
        }

        if (NameEquals(value, "numeric"))
        {
            propertyKind = RegexUnicodePropertyKind.WordBreakNumeric;
            return true;
        }

        return false;
    }

    private static bool TryGetSentenceBreakKind(ReadOnlySpan<byte> value, out RegexUnicodePropertyKind propertyKind)
    {
        propertyKind = RegexUnicodePropertyKind.None;
        if (NameEquals(value, "lower"))
        {
            propertyKind = RegexUnicodePropertyKind.SentenceBreakLower;
            return true;
        }

        if (NameEquals(value, "close"))
        {
            propertyKind = RegexUnicodePropertyKind.SentenceBreakClose;
            return true;
        }

        if (NameEquals(value, "scontinue") || NameEquals(value, "sc"))
        {
            propertyKind = RegexUnicodePropertyKind.SentenceBreakSContinue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Compares a pattern property name using regex-syntax's loose property-name matching.
    /// </summary>
    /// <param name="name">The UTF-8 property name bytes from the regex pattern.</param>
    /// <param name="expected">The lowercase canonical name without ignored separators.</param>
    /// <returns><see langword="true" /> when the names are equivalent.</returns>
    public static bool NameEquals(ReadOnlySpan<byte> name, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        int expectedIndex = 0;
        for (int index = 0; index < name.Length; index++)
        {
            byte value = name[index];
            if (value is (byte)'_' or (byte)'-' or (byte)' ')
            {
                continue;
            }

            if (expectedIndex >= expected.Length)
            {
                return false;
            }

            if (value is >= (byte)'A' and <= (byte)'Z')
            {
                value = (byte)(value + 32);
            }

            if (value != expected[expectedIndex])
            {
                return false;
            }

            expectedIndex++;
        }

        return expectedIndex == expected.Length;
    }
}
