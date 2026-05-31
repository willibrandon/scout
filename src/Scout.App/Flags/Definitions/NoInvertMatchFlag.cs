using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoInvertMatchFlag : IFlag<NoInvertMatchFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-invert-match",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Disable inverted line matching.",
        static lowArgs =>
        {
            lowArgs.SetInvertMatch(false);
            return null;
        });
}
