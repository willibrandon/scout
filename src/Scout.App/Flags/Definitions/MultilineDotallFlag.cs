using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(53)]
internal readonly struct MultilineDotallFlag : IFlag<MultilineDotallFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--multiline-dotall",
        shortName: null,
        "--no-multiline-dotall",
        aliases: [],
        FlagCategory.Regex,
        "Make dot match line terminators in multiline mode.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetMultilineDotall(matchedName != "--no-multiline-dotall");
            return null;
        });
}
