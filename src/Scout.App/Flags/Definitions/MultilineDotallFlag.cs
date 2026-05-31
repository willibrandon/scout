using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct MultilineDotallFlag : IFlag<MultilineDotallFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--multiline-dotall",
        shortName: null,
        "--no-multiline-dotall",
        aliases: [],
        FlagCategory.Regex,
        "Make dot match line terminators in multiline mode.",
        static lowArgs =>
        {
            lowArgs.SetMultilineDotall(true);
            return null;
        });
}
