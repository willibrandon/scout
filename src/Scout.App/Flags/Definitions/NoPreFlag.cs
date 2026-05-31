using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoPreFlag : IFlag<NoPreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-pre",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Disable preprocessing.",
        static lowArgs =>
        {
            lowArgs.SetPreprocessor(null);
            return null;
        });
}
