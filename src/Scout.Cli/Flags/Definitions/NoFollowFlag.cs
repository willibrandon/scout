using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoFollowFlag : IFlag<NoFollowFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-follow",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not follow symbolic links while traversing directories.",
        static lowArgs =>
        {
            lowArgs.SetFollowLinks(false);
            return null;
        });
}
