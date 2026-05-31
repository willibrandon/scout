using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoIgnoreDotFlag : IFlag<NoIgnoreDotFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-dot",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect .ignore and .rgignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectDotIgnoreFiles(false);
            return null;
        });
}
