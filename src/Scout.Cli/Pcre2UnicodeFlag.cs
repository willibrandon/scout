namespace Scout;

internal readonly struct Pcre2UnicodeFlag : IFlag<Pcre2UnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--pcre2-unicode",
        shortName: null,
        "--no-pcre2-unicode",
        aliases: [],
        FlagCategory.Regex,
        "Enable PCRE2 Unicode mode.",
        static lowArgs =>
        {
            lowArgs.SetPcre2Unicode(true);
            return null;
        });
}
