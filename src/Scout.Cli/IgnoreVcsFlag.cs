namespace Scout;

internal readonly struct IgnoreVcsFlag : IFlag<IgnoreVcsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-vcs",
        shortName: null,
        "--no-ignore-vcs",
        aliases: [],
        FlagCategory.Search,
        "Respect source-control ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGitIgnoreFiles(true);
            lowArgs.SetRespectGitExcludeFiles(true);
            return null;
        });
}
