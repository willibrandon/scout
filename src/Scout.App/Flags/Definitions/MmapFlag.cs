using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct MmapFlag : IFlag<MmapFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--mmap",
        shortName: null,
        "--no-mmap",
        aliases: [],
        FlagCategory.Search,
        "Try memory-map searching when possible.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetMmapMode(matchedName == "--no-mmap" ? CliMmapMode.Never : CliMmapMode.AlwaysTryMmap);
            return null;
        });
}
