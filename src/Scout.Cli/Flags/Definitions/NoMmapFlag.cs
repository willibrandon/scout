using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoMmapFlag : IFlag<NoMmapFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-mmap",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Disable memory-map searching.",
        static lowArgs =>
        {
            lowArgs.SetMmapMode(CliMmapMode.Never);
            return null;
        });
}
