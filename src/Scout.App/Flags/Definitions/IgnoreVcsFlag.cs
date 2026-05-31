using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreVcsFlag : IFlag<IgnoreVcsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-vcs",
        shortName: null,
        "--no-ignore-vcs",
        aliases: [],
        FlagCategory.Search,
        "Respect source-control ignore files.",
        static (lowArgs, matchedName) =>
        {
            bool respect = matchedName != "--no-ignore-vcs";
            lowArgs.SetRespectGitIgnoreFiles(respect);
            lowArgs.SetRespectGitExcludeFiles(respect);
            return null;
        });
}
