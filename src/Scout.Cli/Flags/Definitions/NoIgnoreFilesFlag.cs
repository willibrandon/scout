using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoIgnoreFilesFlag : IFlag<NoIgnoreFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-files",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect explicitly supplied ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectExplicitIgnoreFiles(false);
            return null;
        });
}
