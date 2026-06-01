using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(73)]
internal readonly struct Pcre2VersionFlag : IFlag<Pcre2VersionFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Special(
        "--pcre2-version",
        shortName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Print PCRE2 version information.",
        static _ => CliSpecialMode.Pcre2Version);
}
