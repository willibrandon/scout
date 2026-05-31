using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreExcludeFlag : IFlag<IgnoreExcludeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-exclude",
        shortName: null,
        "--no-ignore-exclude",
        aliases: [],
        FlagCategory.Search,
        "Respect git exclude files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectGitExcludeFiles(matchedName != "--no-ignore-exclude");
            return null;
        });
}
