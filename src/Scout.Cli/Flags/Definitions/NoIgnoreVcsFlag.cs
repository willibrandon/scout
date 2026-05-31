using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoIgnoreVcsFlag : IFlag<NoIgnoreVcsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-vcs",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect source-control ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGitIgnoreFiles(false);
            lowArgs.SetRespectGitExcludeFiles(false);
            return null;
        });
}
