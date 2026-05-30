namespace Scout;

internal readonly struct NoPcre2UnicodeFlag : IFlag<NoPcre2UnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-pcre2-unicode",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Disable PCRE2 Unicode mode.",
        static lowArgs =>
        {
            lowArgs.SetPcre2Unicode(false);
            return null;
        });
}
