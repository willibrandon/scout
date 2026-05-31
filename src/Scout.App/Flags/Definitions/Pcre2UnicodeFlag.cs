using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct Pcre2UnicodeFlag : IFlag<Pcre2UnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--pcre2-unicode",
        shortName: null,
        "--no-pcre2-unicode",
        aliases: [],
        FlagCategory.Regex,
        "Enable PCRE2 Unicode mode.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetPcre2Unicode(matchedName != "--no-pcre2-unicode");
            return null;
        });
}
