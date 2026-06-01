using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(25)]
internal readonly struct FixedStringsFlag : IFlag<FixedStringsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--fixed-strings",
        'F',
        "--no-fixed-strings",
        aliases: [],
        FlagCategory.Matching,
        "Treat patterns as literal strings.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetFixedStrings(matchedName != "--no-fixed-strings");
            return null;
        });
}
