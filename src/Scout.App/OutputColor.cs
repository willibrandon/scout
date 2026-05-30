using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

internal readonly struct OutputColor
{
    private const int OutputCount = 5;
    private const int StyleUnset = -1;
    private const int StyleFalse = 0;
    private const int StyleTrue = 1;
    private static readonly Encoding AnsiEncoding = Encoding.ASCII;

    private readonly ReadOnlyMemory<byte> pathStart;
    private readonly ReadOnlyMemory<byte> lineStart;
    private readonly ReadOnlyMemory<byte> numberStart;
    private readonly ReadOnlyMemory<byte> matchStart;
    private readonly ReadOnlyMemory<byte> highlightStart;

    public OutputColor(bool enabled)
    {
        Enabled = enabled;
        pathStart = enabled ? "\u001b[0m\u001b[35m"u8.ToArray() : ReadOnlyMemory<byte>.Empty;
        lineStart = enabled ? "\u001b[0m\u001b[32m"u8.ToArray() : ReadOnlyMemory<byte>.Empty;
        numberStart = enabled ? "\u001b[0m"u8.ToArray() : ReadOnlyMemory<byte>.Empty;
        matchStart = enabled ? "\u001b[0m\u001b[1m\u001b[31m"u8.ToArray() : ReadOnlyMemory<byte>.Empty;
        highlightStart = ReadOnlyMemory<byte>.Empty;
    }

    private OutputColor(
        bool enabled,
        ReadOnlyMemory<byte> pathStart,
        ReadOnlyMemory<byte> lineStart,
        ReadOnlyMemory<byte> numberStart,
        ReadOnlyMemory<byte> matchStart,
        ReadOnlyMemory<byte> highlightStart,
        bool highlightEnabled)
    {
        Enabled = enabled;
        this.pathStart = pathStart;
        this.lineStart = lineStart;
        this.numberStart = numberStart;
        this.matchStart = matchStart;
        this.highlightStart = highlightStart;
        HighlightEnabled = highlightEnabled;
    }

    public bool Enabled { get; }

    public bool HighlightEnabled { get; }

    public static OutputColor Create(bool enabled, IReadOnlyList<string> colorSpecs)
    {
        ArgumentNullException.ThrowIfNull(colorSpecs);
        if (!enabled)
        {
            return new OutputColor(false);
        }

        int[] fgKind = new int[OutputCount];
        int[] fgA = new int[OutputCount];
        int[] fgB = new int[OutputCount];
        int[] fgC = new int[OutputCount];
        int[] bgKind = new int[OutputCount];
        int[] bgA = new int[OutputCount];
        int[] bgB = new int[OutputCount];
        int[] bgC = new int[OutputCount];
        int[] bold = CreateStyleArray();
        int[] intense = CreateStyleArray();
        int[] underline = CreateStyleArray();
        int[] italic = CreateStyleArray();

        SetBasicColor(fgKind, fgA, CliColorSpecParser.OutputPath, 5);
        SetBasicColor(fgKind, fgA, CliColorSpecParser.OutputLine, 2);
        SetBasicColor(fgKind, fgA, CliColorSpecParser.OutputMatch, 3);
        bold[CliColorSpecParser.OutputMatch] = StyleTrue;

        for (int index = 0; index < colorSpecs.Count; index++)
        {
            string spec = colorSpecs[index];
            if (!CliColorSpecParser.TryParse(
                spec,
                out int outputType,
                out int specType,
                out int colorKind,
                out int colorA,
                out int colorB,
                out int colorC,
                out int style,
                out string? error))
            {
                throw new InvalidOperationException(error);
            }

            ApplySpec(
                outputType,
                specType,
                colorKind,
                colorA,
                colorB,
                colorC,
                style,
                fgKind,
                fgA,
                fgB,
                fgC,
                bgKind,
                bgA,
                bgB,
                bgC,
                bold,
                intense,
                underline,
                italic);
        }

        bool highlightEnabled = HasStyle(
            CliColorSpecParser.OutputHighlight,
            fgKind,
            bgKind,
            bold,
            intense,
            underline,
            italic);

        return new OutputColor(
            enabled: true,
            BuildPrefix(CliColorSpecParser.OutputPath, fgKind, fgA, fgB, fgC, bgKind, bgA, bgB, bgC, bold, intense, underline, italic),
            BuildPrefix(CliColorSpecParser.OutputLine, fgKind, fgA, fgB, fgC, bgKind, bgA, bgB, bgC, bold, intense, underline, italic),
            BuildPrefix(CliColorSpecParser.OutputColumn, fgKind, fgA, fgB, fgC, bgKind, bgA, bgB, bgC, bold, intense, underline, italic),
            BuildPrefix(CliColorSpecParser.OutputMatch, fgKind, fgA, fgB, fgC, bgKind, bgA, bgB, bgC, bold, intense, underline, italic),
            BuildPrefix(CliColorSpecParser.OutputHighlight, fgKind, fgA, fgB, fgC, bgKind, bgA, bgB, bgC, bold, intense, underline, italic),
            highlightEnabled);
    }

    public void WritePathStart(RawByteWriter output)
    {
        if (Enabled)
        {
            output.Write(pathStart.Span);
        }
    }

    public void WriteLineNumberStart(RawByteWriter output)
    {
        if (Enabled)
        {
            output.Write(lineStart.Span);
        }
    }

    public void WriteNumberStart(RawByteWriter output)
    {
        if (Enabled)
        {
            output.Write(numberStart.Span);
        }
    }

    public void WriteMatchStart(RawByteWriter output)
    {
        if (Enabled)
        {
            output.Write(matchStart.Span);
        }
    }

    public void WriteHighlightStart(RawByteWriter output)
    {
        if (Enabled && HighlightEnabled)
        {
            output.Write(highlightStart.Span);
        }
    }

    public void WriteReset(RawByteWriter output)
    {
        if (Enabled)
        {
            output.Write("\u001b[0m"u8);
        }
    }

    public void WritePath(RawByteWriter output, ReadOnlySpan<byte> path)
    {
        WritePathStart(output);
        output.Write(path);
        WriteReset(output);
    }

    public void WriteLineNumber(RawByteWriter output, long value)
    {
        WriteLineNumberStart(output);
        WriteNumber(output, value);
        WriteReset(output);
    }

    public void WriteNumberField(RawByteWriter output, long value)
    {
        WriteNumberStart(output);
        WriteNumber(output, value);
        WriteReset(output);
    }

    public void WriteMatch(RawByteWriter output, ReadOnlySpan<byte> value)
    {
        WriteMatchStart(output);
        output.Write(value);
        WriteReset(output);
    }

    public static void WriteNumber(RawByteWriter output, long value)
    {
        Span<byte> buffer = stackalloc byte[20];
        ulong number = (ulong)value;
        int index = buffer.Length;
        do
        {
            index--;
            buffer[index] = (byte)((number % 10) + (byte)'0');
            number /= 10;
        }
        while (number != 0);

        output.Write(buffer[index..]);
    }

    private static int[] CreateStyleArray()
    {
        int[] values = new int[OutputCount];
        Array.Fill(values, StyleUnset);
        return values;
    }

    private static void ApplySpec(
        int outputType,
        int specType,
        int colorKind,
        int colorA,
        int colorB,
        int colorC,
        int style,
        int[] fgKind,
        int[] fgA,
        int[] fgB,
        int[] fgC,
        int[] bgKind,
        int[] bgA,
        int[] bgB,
        int[] bgC,
        int[] bold,
        int[] intense,
        int[] underline,
        int[] italic)
    {
        switch (specType)
        {
            case CliColorSpecParser.SpecNone:
                fgKind[outputType] = CliColorSpecParser.ColorNone;
                bgKind[outputType] = CliColorSpecParser.ColorNone;
                bold[outputType] = StyleUnset;
                intense[outputType] = StyleUnset;
                underline[outputType] = StyleUnset;
                italic[outputType] = StyleUnset;
                break;

            case CliColorSpecParser.SpecFg:
                fgKind[outputType] = colorKind;
                fgA[outputType] = colorA;
                fgB[outputType] = colorB;
                fgC[outputType] = colorC;
                break;

            case CliColorSpecParser.SpecBg:
                bgKind[outputType] = colorKind;
                bgA[outputType] = colorA;
                bgB[outputType] = colorB;
                bgC[outputType] = colorC;
                break;

            case CliColorSpecParser.SpecStyle:
                ApplyStyle(outputType, style, bold, intense, underline, italic);
                break;
        }
    }

    private static void ApplyStyle(int outputType, int style, int[] bold, int[] intense, int[] underline, int[] italic)
    {
        switch (style)
        {
            case CliColorSpecParser.StyleBold:
                bold[outputType] = StyleTrue;
                break;

            case CliColorSpecParser.StyleNoBold:
                bold[outputType] = StyleFalse;
                break;

            case CliColorSpecParser.StyleIntense:
                intense[outputType] = StyleTrue;
                break;

            case CliColorSpecParser.StyleNoIntense:
                intense[outputType] = StyleFalse;
                break;

            case CliColorSpecParser.StyleUnderline:
                underline[outputType] = StyleTrue;
                break;

            case CliColorSpecParser.StyleNoUnderline:
                underline[outputType] = StyleFalse;
                break;

            case CliColorSpecParser.StyleItalic:
                italic[outputType] = StyleTrue;
                break;

            case CliColorSpecParser.StyleNoItalic:
                italic[outputType] = StyleFalse;
                break;
        }
    }

    private static bool HasStyle(int outputType, int[] fgKind, int[] bgKind, int[] bold, int[] intense, int[] underline, int[] italic)
    {
        return fgKind[outputType] != CliColorSpecParser.ColorNone ||
            bgKind[outputType] != CliColorSpecParser.ColorNone ||
            bold[outputType] == StyleTrue ||
            intense[outputType] == StyleTrue ||
            underline[outputType] == StyleTrue ||
            italic[outputType] == StyleTrue;
    }

    private static ReadOnlyMemory<byte> BuildPrefix(
        int outputType,
        int[] fgKind,
        int[] fgA,
        int[] fgB,
        int[] fgC,
        int[] bgKind,
        int[] bgA,
        int[] bgB,
        int[] bgC,
        int[] bold,
        int[] intense,
        int[] underline,
        int[] italic)
    {
        var builder = new StringBuilder();
        AppendCode(builder, "0");
        if (bold[outputType] == StyleTrue)
        {
            AppendCode(builder, "1");
        }

        if (italic[outputType] == StyleTrue)
        {
            AppendCode(builder, "3");
        }

        if (underline[outputType] == StyleTrue)
        {
            AppendCode(builder, "4");
        }

        AppendColor(builder, foreground: true, fgKind[outputType], fgA[outputType], fgB[outputType], fgC[outputType], intense[outputType] == StyleTrue);
        AppendColor(builder, foreground: false, bgKind[outputType], bgA[outputType], bgB[outputType], bgC[outputType], intense[outputType] == StyleTrue);
        return AnsiEncoding.GetBytes(builder.ToString());
    }

    private static void AppendColor(StringBuilder builder, bool foreground, int kind, int a, int b, int c, bool intense)
    {
        switch (kind)
        {
            case CliColorSpecParser.ColorBasic:
                if (intense)
                {
                    AppendCode(builder, $"{(foreground ? 38 : 48)};5;{GetIntenseBasicColor(a)}");
                }
                else
                {
                    AppendCode(builder, $"{(foreground ? 30 : 40) + GetBasicAnsiOffset(a)}");
                }

                break;

            case CliColorSpecParser.ColorAnsi256:
                AppendCode(builder, $"{(foreground ? 38 : 48)};5;{a}");
                break;

            case CliColorSpecParser.ColorRgb:
                AppendCode(builder, $"{(foreground ? 38 : 48)};2;{a};{b};{c}");
                break;
        }
    }

    private static int GetBasicAnsiOffset(int color)
    {
        return color switch
        {
            0 => 0,
            1 => 4,
            2 => 2,
            3 => 1,
            4 => 6,
            5 => 5,
            6 => 3,
            _ => 7,
        };
    }

    private static int GetIntenseBasicColor(int color)
    {
        return color switch
        {
            0 => 8,
            1 => 12,
            2 => 10,
            3 => 9,
            4 => 14,
            5 => 13,
            6 => 11,
            _ => 15,
        };
    }

    private static void SetBasicColor(int[] colorKind, int[] colorA, int outputType, int color)
    {
        colorKind[outputType] = CliColorSpecParser.ColorBasic;
        colorA[outputType] = color;
    }

    private static void AppendCode(StringBuilder builder, string code)
    {
        builder.Append("\u001b[");
        builder.Append(code);
        builder.Append('m');
    }
}
