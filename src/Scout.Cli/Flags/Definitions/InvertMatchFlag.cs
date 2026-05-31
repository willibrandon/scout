using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct InvertMatchFlag : IFlag<InvertMatchFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--invert-match",
        'v',
        "--no-invert-match",
        aliases: [],
        FlagCategory.Matching,
        "Invert line matching.",
        static lowArgs =>
        {
            lowArgs.SetInvertMatch(true);
            return null;
        });
}
