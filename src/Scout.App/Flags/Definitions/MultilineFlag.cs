using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(52)]
internal readonly struct MultilineFlag : IFlag<MultilineFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--multiline",
        'U',
        "--no-multiline",
        aliases: [],
        FlagCategory.Regex,
        "Permit matches to span line terminators.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetMultiline(matchedName != "--no-multiline");
            return null;
        });
}
