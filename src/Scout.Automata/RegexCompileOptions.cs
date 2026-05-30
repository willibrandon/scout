namespace Scout;

internal readonly struct RegexCompileOptions
{
    public RegexCompileOptions(bool caseInsensitive, bool swapGreed, bool multiLine, bool dotMatchesNewline)
    {
        CaseInsensitive = caseInsensitive;
        SwapGreed = swapGreed;
        MultiLine = multiLine;
        DotMatchesNewline = dotMatchesNewline;
    }

    public bool CaseInsensitive { get; }

    public bool SwapGreed { get; }

    public bool MultiLine { get; }

    public bool DotMatchesNewline { get; }

    public RegexCompileOptions Apply(string enabledFlags, string disabledFlags)
    {
        bool caseInsensitive = CaseInsensitive;
        bool swapGreed = SwapGreed;
        bool multiLine = MultiLine;
        bool dotMatchesNewline = DotMatchesNewline;
        for (int index = 0; index < enabledFlags.Length; index++)
        {
            ApplyFlag(enabledFlags[index], enabled: true, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline);
        }

        for (int index = 0; index < disabledFlags.Length; index++)
        {
            ApplyFlag(disabledFlags[index], enabled: false, ref caseInsensitive, ref swapGreed, ref multiLine, ref dotMatchesNewline);
        }

        return new RegexCompileOptions(caseInsensitive, swapGreed, multiLine, dotMatchesNewline);
    }

    private static void ApplyFlag(
        char flag,
        bool enabled,
        ref bool caseInsensitive,
        ref bool swapGreed,
        ref bool multiLine,
        ref bool dotMatchesNewline)
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
        }
    }
}
