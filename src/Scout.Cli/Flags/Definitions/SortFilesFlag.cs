using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct SortFilesFlag : IFlag<SortFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--sort-files",
        shortName: null,
        "--no-sort-files",
        aliases: [],
        FlagCategory.Search,
        "Sort results by path.",
        static lowArgs =>
        {
            lowArgs.SetSortMode(new CliSortMode(reverse: false, CliSortKind.Path));
            return null;
        });
}
