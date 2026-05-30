namespace Scout;

internal readonly struct MmapFlag : IFlag<MmapFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--mmap",
        shortName: null,
        "--no-mmap",
        aliases: [],
        FlagCategory.Search,
        "Try memory-map searching when possible.",
        static lowArgs =>
        {
            lowArgs.SetMmapMode(CliMmapMode.AlwaysTryMmap);
            return null;
        });
}
