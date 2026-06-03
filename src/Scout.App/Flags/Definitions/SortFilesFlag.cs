
namespace Scout.Flags.Definitions;

[FlagOrder(103)]
internal readonly struct SortFilesFlag : IFlag<SortFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--sort-files",
        shortName: null,
        "--no-sort-files",
        aliases: [],
        FlagCategory.Search,
        "Sort results by path.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetSortMode(matchedName == "--no-sort-files" ? null : new CliSortMode(reverse: false, CliSortKind.Path));
            return null;
        });
}
