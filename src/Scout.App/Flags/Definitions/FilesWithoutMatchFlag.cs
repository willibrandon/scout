
namespace Scout.Flags.Definitions;

[FlagOrder(24)]
internal readonly struct FilesWithoutMatchFlag : IFlag<FilesWithoutMatchFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--files-without-match",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Print only the paths with no matches.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.FilesWithoutMatch);
            return null;
        });
}
