namespace Scout;

internal readonly struct IgnoreFilesFlag : IFlag<IgnoreFilesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-files",
        shortName: null,
        "--no-ignore-files",
        aliases: [],
        FlagCategory.Search,
        "Respect explicitly supplied ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectExplicitIgnoreFiles(true);
            return null;
        });
}
