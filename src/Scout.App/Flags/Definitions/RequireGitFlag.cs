using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct RequireGitFlag : IFlag<RequireGitFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--require-git",
        shortName: null,
        "--no-require-git",
        aliases: [],
        FlagCategory.Search,
        "Require a git repository for gitignore files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRequireGitRepository(matchedName != "--no-require-git");
            return null;
        });
}
