using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreDotFlag : IFlag<IgnoreDotFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-dot",
        shortName: null,
        "--no-ignore-dot",
        aliases: [],
        FlagCategory.Search,
        "Respect .ignore and .rgignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectDotIgnoreFiles(true);
            return null;
        });
}
