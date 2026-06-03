
namespace Scout.Flags.Definitions;

[FlagOrder(84)]
internal readonly struct StatsFlag : IFlag<StatsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--stats",
        shortName: null,
        "--no-stats",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print final search statistics.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetStats(matchedName != "--no-stats");
            return null;
        });
}
