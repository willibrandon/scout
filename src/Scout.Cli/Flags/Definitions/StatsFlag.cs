namespace Scout.Flags.Definitions;

internal readonly struct StatsFlag : IFlag<StatsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--stats",
        shortName: null,
        "--no-stats",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print final search statistics.",
        static lowArgs =>
        {
            lowArgs.SetStats(true);
            return null;
        });
}
