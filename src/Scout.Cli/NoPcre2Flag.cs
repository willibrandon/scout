namespace Scout;

internal readonly struct NoPcre2Flag : IFlag<NoPcre2Flag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-pcre2",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Use Scout's default regex engine.",
        static lowArgs =>
        {
            lowArgs.SetRegexEngine(CliRegexEngine.Default);
            return null;
        });
}
