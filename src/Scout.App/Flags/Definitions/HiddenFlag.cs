using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct HiddenFlag : IFlag<HiddenFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--hidden",
        '.',
        "--no-hidden",
        aliases: [],
        FlagCategory.Search,
        "Search hidden files and directories.",
        static lowArgs =>
        {
            lowArgs.SetIncludeHidden(true);
            return null;
        });
}
