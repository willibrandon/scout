using System;
using System.Globalization;

namespace Scout;

/// <summary>
/// Parses ripgrep-compatible <c>--colors</c> specifications.
/// </summary>
public static class CliColorSpecParser
{
    /// <summary>Identifies path output styling.</summary>
    public const int OutputPath = 0;
    /// <summary>Identifies line-number output styling.</summary>
    public const int OutputLine = 1;
    /// <summary>Identifies column-number output styling.</summary>
    public const int OutputColumn = 2;
    /// <summary>Identifies matched-text output styling.</summary>
    public const int OutputMatch = 3;
    /// <summary>Identifies whole-line highlight output styling.</summary>
    public const int OutputHighlight = 4;
    /// <summary>Identifies foreground color specifications.</summary>
    public const int SpecFg = 0;
    /// <summary>Identifies background color specifications.</summary>
    public const int SpecBg = 1;
    /// <summary>Identifies style attribute specifications.</summary>
    public const int SpecStyle = 2;
    /// <summary>Identifies reset specifications.</summary>
    public const int SpecNone = 3;
    /// <summary>Identifies the absence of a parsed color.</summary>
    public const int ColorNone = 0;
    /// <summary>Identifies a named basic ANSI color.</summary>
    public const int ColorBasic = 1;
    /// <summary>Identifies an ANSI 256-color value.</summary>
    public const int ColorAnsi256 = 2;
    /// <summary>Identifies a true-color RGB value.</summary>
    public const int ColorRgb = 3;
    /// <summary>Identifies the bold style.</summary>
    public const int StyleBold = 0;
    /// <summary>Identifies clearing the bold style.</summary>
    public const int StyleNoBold = 1;
    /// <summary>Identifies the intense style.</summary>
    public const int StyleIntense = 2;
    /// <summary>Identifies clearing the intense style.</summary>
    public const int StyleNoIntense = 3;
    /// <summary>Identifies the underline style.</summary>
    public const int StyleUnderline = 4;
    /// <summary>Identifies clearing the underline style.</summary>
    public const int StyleNoUnderline = 5;
    /// <summary>Identifies the italic style.</summary>
    public const int StyleItalic = 6;
    /// <summary>Identifies clearing the italic style.</summary>
    public const int StyleNoItalic = 7;

    /// <summary>
    /// Parses a single <c>--colors</c> specification into integer tags used by command-line validation and output rendering.
    /// </summary>
    /// <param name="value">The color specification text.</param>
    /// <param name="outputType">The parsed output region tag.</param>
    /// <param name="specType">The parsed specification kind tag.</param>
    /// <param name="colorKind">The parsed color representation tag.</param>
    /// <param name="colorA">The first color component.</param>
    /// <param name="colorB">The second color component.</param>
    /// <param name="colorC">The third color component.</param>
    /// <param name="style">The parsed style tag.</param>
    /// <param name="error">The ripgrep-compatible parse error, when parsing fails.</param>
    /// <returns><see langword="true" /> when parsing succeeds.</returns>
    public static bool TryParse(
        string value,
        out int outputType,
        out int specType,
        out int colorKind,
        out int colorA,
        out int colorB,
        out int colorC,
        out int style,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);
        outputType = 0;
        specType = 0;
        colorKind = ColorNone;
        colorA = 0;
        colorB = 0;
        colorC = 0;
        style = 0;

        string[] pieces = value.Split(':');
        if (pieces.Length <= 1 || pieces.Length > 3)
        {
            error = InvalidFormat(value);
            return false;
        }

        if (!TryParseOutputType(pieces[0], out outputType, out error))
        {
            return false;
        }

        if (!TryParseSpecType(pieces[1], out specType, out error))
        {
            return false;
        }

        if (specType == SpecNone)
        {
            error = null;
            return true;
        }

        if (pieces.Length < 3)
        {
            error = InvalidFormat(value);
            return false;
        }

        if (specType == SpecStyle)
        {
            return TryParseStyle(pieces[2], out style, out error);
        }

        return TryParseColor(pieces[2], out colorKind, out colorA, out colorB, out colorC, out error);
    }

    private static bool TryParseOutputType(string value, out int outputType, out string? error)
    {
        if (string.Equals(value, "path", StringComparison.OrdinalIgnoreCase))
        {
            outputType = OutputPath;
            error = null;
            return true;
        }

        if (string.Equals(value, "line", StringComparison.OrdinalIgnoreCase))
        {
            outputType = OutputLine;
            error = null;
            return true;
        }

        if (string.Equals(value, "column", StringComparison.OrdinalIgnoreCase))
        {
            outputType = OutputColumn;
            error = null;
            return true;
        }

        if (string.Equals(value, "match", StringComparison.OrdinalIgnoreCase))
        {
            outputType = OutputMatch;
            error = null;
            return true;
        }

        if (string.Equals(value, "highlight", StringComparison.OrdinalIgnoreCase))
        {
            outputType = OutputHighlight;
            error = null;
            return true;
        }

        outputType = 0;
        error = $"unrecognized output type '{value}'. Choose from: path, line, column, match, highlight.";
        return false;
    }

    private static bool TryParseSpecType(string value, out int specType, out string? error)
    {
        if (string.Equals(value, "fg", StringComparison.OrdinalIgnoreCase))
        {
            specType = SpecFg;
            error = null;
            return true;
        }

        if (string.Equals(value, "bg", StringComparison.OrdinalIgnoreCase))
        {
            specType = SpecBg;
            error = null;
            return true;
        }

        if (string.Equals(value, "style", StringComparison.OrdinalIgnoreCase))
        {
            specType = SpecStyle;
            error = null;
            return true;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            specType = SpecNone;
            error = null;
            return true;
        }

        specType = 0;
        error = $"unrecognized spec type '{value}'. Choose from: fg, bg, style, none.";
        return false;
    }

    private static bool TryParseStyle(string value, out int style, out string? error)
    {
        if (string.Equals(value, "bold", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleBold;
            error = null;
            return true;
        }

        if (string.Equals(value, "nobold", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleNoBold;
            error = null;
            return true;
        }

        if (string.Equals(value, "intense", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleIntense;
            error = null;
            return true;
        }

        if (string.Equals(value, "nointense", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleNoIntense;
            error = null;
            return true;
        }

        if (string.Equals(value, "underline", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleUnderline;
            error = null;
            return true;
        }

        if (string.Equals(value, "nounderline", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleNoUnderline;
            error = null;
            return true;
        }

        if (string.Equals(value, "italic", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleItalic;
            error = null;
            return true;
        }

        if (string.Equals(value, "noitalic", StringComparison.OrdinalIgnoreCase))
        {
            style = StyleNoItalic;
            error = null;
            return true;
        }

        style = 0;
        error = $"unrecognized style attribute '{value}'. Choose from: nobold, bold, nointense, intense, nounderline, underline, noitalic, italic.";
        return false;
    }

    private static bool TryParseColor(string value, out int kind, out int a, out int b, out int c, out string? error)
    {
        if (TryParseBasicColor(value, out a))
        {
            kind = ColorBasic;
            b = 0;
            c = 0;
            error = null;
            return true;
        }

        if (value.Contains(',', StringComparison.Ordinal))
        {
            string[] pieces = value.Split(',');
            if (pieces.Length == 3 &&
                TryParseByteComponent(pieces[0], out a) &&
                TryParseByteComponent(pieces[1], out b) &&
                TryParseByteComponent(pieces[2], out c))
            {
                kind = ColorRgb;
                error = null;
                return true;
            }

            kind = ColorNone;
            a = 0;
            b = 0;
            c = 0;
            error = $"unrecognized RGB color triple, should be '[0-255],[0-255],[0-255]' (or a hex triple), but is '{value}'";
            return false;
        }

        if (IsDecimal(value))
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed <= byte.MaxValue)
            {
                kind = ColorAnsi256;
                a = parsed;
                b = 0;
                c = 0;
                error = null;
                return true;
            }

            kind = ColorNone;
            a = 0;
            b = 0;
            c = 0;
            error = $"unrecognized ansi256 color number, should be '[0-255]' (or a hex number), but is '{value}'";
            return false;
        }

        if (IsHex(value) && TryParseHexByte(value, out a))
        {
            kind = ColorAnsi256;
            b = 0;
            c = 0;
            error = null;
            return true;
        }

        kind = ColorNone;
        a = 0;
        b = 0;
        c = 0;
        error = $"unrecognized color name '{value}'. Choose from: black, blue, green, red, cyan, magenta, yellow, white";
        return false;
    }

    private static bool TryParseBasicColor(string value, out int color)
    {
        if (string.Equals(value, "black", StringComparison.OrdinalIgnoreCase))
        {
            color = 0;
            return true;
        }

        if (string.Equals(value, "blue", StringComparison.OrdinalIgnoreCase))
        {
            color = 1;
            return true;
        }

        if (string.Equals(value, "green", StringComparison.OrdinalIgnoreCase))
        {
            color = 2;
            return true;
        }

        if (string.Equals(value, "red", StringComparison.OrdinalIgnoreCase))
        {
            color = 3;
            return true;
        }

        if (string.Equals(value, "cyan", StringComparison.OrdinalIgnoreCase))
        {
            color = 4;
            return true;
        }

        if (string.Equals(value, "magenta", StringComparison.OrdinalIgnoreCase))
        {
            color = 5;
            return true;
        }

        if (string.Equals(value, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            color = 6;
            return true;
        }

        if (string.Equals(value, "white", StringComparison.OrdinalIgnoreCase))
        {
            color = 7;
            return true;
        }

        color = 0;
        return false;
    }

    private static bool TryParseByteComponent(string value, out int parsed)
    {
        if (IsDecimal(value))
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed <= byte.MaxValue;
        }

        if (IsHex(value))
        {
            return TryParseHexByte(value, out parsed);
        }

        parsed = 0;
        return false;
    }

    private static bool TryParseHexByte(string value, out int parsed)
    {
        return int.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed) &&
            parsed <= byte.MaxValue;
    }

    private static bool IsDecimal(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] < '0' || value[index] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHex(string value)
    {
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.Length == 2)
        {
            return false;
        }

        for (int index = 2; index < value.Length; index++)
        {
            char c = value[index];
            if ((c < '0' || c > '9') &&
                (c < 'a' || c > 'f') &&
                (c < 'A' || c > 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static string InvalidFormat(string value)
    {
        return $"invalid color spec format: '{value}'. Valid format is '(path|line|column|match|highlight):(fg|bg|style):(value)'.";
    }
}
