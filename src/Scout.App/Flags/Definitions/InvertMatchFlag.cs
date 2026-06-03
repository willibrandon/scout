
namespace Scout.Flags.Definitions;

[FlagOrder(40)]
internal readonly struct InvertMatchFlag : IFlag<InvertMatchFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--invert-match",
        'v',
        "--no-invert-match",
        aliases: [],
        FlagCategory.Matching,
        "Invert line matching.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetInvertMatch(matchedName != "--no-invert-match");
            return null;
        });
}
