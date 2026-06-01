using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(22)]
internal readonly struct FilesFlag : IFlag<FilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--files",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Print each file that would be searched.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.Files);
            return null;
        });
}
