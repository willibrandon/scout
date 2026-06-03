
namespace Scout.Flags.Definitions;

[FlagOrder(85)]
internal readonly struct StopOnNonmatchFlag : IFlag<StopOnNonmatchFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--stop-on-nonmatch",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Stop searching after a non-matching line following a match.",
        static lowArgs =>
        {
            lowArgs.SetStopOnNonmatch(true);
            return null;
        });
}
