using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoJsonFlag : IFlag<NoJsonFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-json",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Disable JSON Lines output.",
        static lowArgs =>
        {
            lowArgs.ClearJsonMode();
            return null;
        });
}
