using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct GlobCaseInsensitiveFlag : IFlag<GlobCaseInsensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--glob-case-insensitive",
        shortName: null,
        "--no-glob-case-insensitive",
        aliases: [],
        FlagCategory.Search,
        "Match glob patterns case insensitively.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetGlobCaseInsensitive(matchedName != "--no-glob-case-insensitive");
            return null;
        });
}
