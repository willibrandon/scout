using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoStatsFlag : IFlag<NoStatsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-stats",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Disable final search statistics.",
        static lowArgs =>
        {
            lowArgs.SetStats(false);
            return null;
        });
}
