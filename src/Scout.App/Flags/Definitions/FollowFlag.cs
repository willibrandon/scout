using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct FollowFlag : IFlag<FollowFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--follow",
        'L',
        "--no-follow",
        aliases: [],
        FlagCategory.Search,
        "Follow symbolic links while traversing directories.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetFollowLinks(matchedName != "--no-follow");
            return null;
        });
}
