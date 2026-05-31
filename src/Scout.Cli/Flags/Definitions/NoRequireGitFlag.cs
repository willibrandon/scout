using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoRequireGitFlag : IFlag<NoRequireGitFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-require-git",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not require a git repository for gitignore files.",
        static lowArgs =>
        {
            lowArgs.SetRequireGitRepository(false);
            return null;
        });
}
