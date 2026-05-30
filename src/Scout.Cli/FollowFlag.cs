namespace Scout;

internal readonly struct FollowFlag : IFlag<FollowFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--follow",
        'L',
        "--no-follow",
        aliases: [],
        FlagCategory.Search,
        "Follow symbolic links while traversing directories.",
        static lowArgs =>
        {
            lowArgs.SetFollowLinks(true);
            return null;
        });
}
