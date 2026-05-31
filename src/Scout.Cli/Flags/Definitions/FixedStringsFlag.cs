using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct FixedStringsFlag : IFlag<FixedStringsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--fixed-strings",
        'F',
        "--no-fixed-strings",
        aliases: [],
        FlagCategory.Matching,
        "Treat patterns as literal strings.",
        static lowArgs =>
        {
            lowArgs.SetFixedStrings(true);
            return null;
        });
}
