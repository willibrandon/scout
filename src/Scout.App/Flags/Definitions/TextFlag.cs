using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(86)]
internal readonly struct TextFlag : IFlag<TextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--text",
        'a',
        "--no-text",
        aliases: [],
        FlagCategory.Binary,
        "Search binary files as if they were text.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetTextMode(matchedName != "--no-text");
            return null;
        });
}
