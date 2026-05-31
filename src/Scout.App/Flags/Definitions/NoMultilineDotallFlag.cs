using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoMultilineDotallFlag : IFlag<NoMultilineDotallFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-multiline-dotall",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Do not make dot match line terminators in multiline mode.",
        static lowArgs =>
        {
            lowArgs.SetMultilineDotall(false);
            return null;
        });
}
