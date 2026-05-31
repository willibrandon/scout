using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreParentFlag : IFlag<IgnoreParentFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-parent",
        shortName: null,
        "--no-ignore-parent",
        aliases: [],
        FlagCategory.Search,
        "Respect ignore files in parent directories.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectParentIgnoreFiles(matchedName != "--no-ignore-parent");
            return null;
        });
}
