using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(32)]
internal readonly struct HiddenFlag : IFlag<HiddenFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--hidden",
        '.',
        "--no-hidden",
        aliases: [],
        FlagCategory.Search,
        "Search hidden files and directories.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetIncludeHidden(matchedName != "--no-hidden");
            return null;
        });
}
