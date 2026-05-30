namespace Scout;

internal readonly struct NoSortFilesFlag : IFlag<NoSortFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-sort-files",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Disable path sorting alias.",
        static lowArgs =>
        {
            lowArgs.SetSortMode(null);
            return null;
        });
}
