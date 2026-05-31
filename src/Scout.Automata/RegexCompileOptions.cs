namespace Scout;

internal readonly struct RegexCompileOptions
{
    public RegexCompileOptions(
        bool caseInsensitive,
        bool swapGreed,
        bool multiLine,
        bool dotMatchesNewline,
        bool crlf = false,
        byte lineTerminator = (byte)'\n',
        bool utf8 = true)
    {
        CaseInsensitive = caseInsensitive;
        SwapGreed = swapGreed;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
        Crlf = crlf;
        LineTerminator = lineTerminator;
        Utf8 = utf8;
    }

    public bool CaseInsensitive { get; }

    public bool SwapGreed { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public bool Crlf { get; }

    public byte LineTerminator { get; }

    public bool Utf8 { get; }

    public RegexCompileOptions Apply(string enabledFlags, string disabledFlags)
    {
        bool caseInsensitive = CaseInsensitive;
        bool swapGreed = SwapGreed;
        bool multiLine = MultiLine;
        bool dotMatchesNewline = DotMatchesNewline;
        bool crlf = Crlf;
        bool utf8 = Utf8;
        for (int index = 0; index < enabledFlags.Length; index++)
        {
            ApplyFlag(enabledFlags[index], enabled: true, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline, ref crlf, ref utf8);
        }

        for (int index = 0; index < disabledFlags.Length; index++)
        {
            ApplyFlag(disabledFlags[index], enabled: false, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline, ref crlf, ref utf8);
        }

        return new RegexCompileOptions(caseInsensitive, swapGreed, multiLine, dotMatchesNewline, crlf, LineTerminator, utf8);
    }

    private static void ApplyFlag(
        char flag,
        bool enabled,
        ref bool caseInsensitive,
        ref bool swapGreed,
        ref bool multiLine,
        ref bool dotMatchesNewline,
        ref bool crlf,
        ref bool utf8)
    {
        switch (flag)
        {
            case 'i':
                caseInsensitive = enabled;
                break;
            case 'm':
                multiLine = enabled;
                break;
            case 's':
                dotMatchesNewline = enabled;
                break;
            case 'U':
                swapGreed = enabled;
                break;
            case 'R':
                crlf = enabled;
                break;
            case 'u':
                utf8 = enabled;
                break;
        }
    }
}
