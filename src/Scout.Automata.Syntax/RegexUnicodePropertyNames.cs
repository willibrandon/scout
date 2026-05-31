using System;

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
