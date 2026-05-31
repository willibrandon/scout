using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreFilesFlag : IFlag<IgnoreFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-files",
        shortName: null,
        "--no-ignore-files",
        aliases: [],
        FlagCategory.Search,
        "Respect explicitly supplied ignore files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectExplicitIgnoreFiles(matchedName != "--no-ignore-files");
            return null;
        });
}
