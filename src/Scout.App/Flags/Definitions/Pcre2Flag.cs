using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(72)]
internal readonly struct Pcre2Flag : IFlag<Pcre2Flag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--pcre2",
        'P',
        "--no-pcre2",
        aliases: [],
        FlagCategory.Regex,
        "Use the PCRE2 regex engine.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRegexEngine(matchedName == "--no-pcre2" ? CliRegexEngine.Default : CliRegexEngine.Pcre2);
            return null;
        });
}
