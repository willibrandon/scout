namespace Scout.Flags.Definitions;

internal readonly struct Pcre2Flag : IFlag<Pcre2Flag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--pcre2",
        'P',
        "--no-pcre2",
        aliases: [],
        FlagCategory.Regex,
        "Use the PCRE2 regex engine.",
        static lowArgs =>
        {
            lowArgs.SetRegexEngine(CliRegexEngine.Pcre2);
            return null;
        });
}
