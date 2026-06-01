using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(31)]
internal readonly struct HelpFlag : IFlag<HelpFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Special(
        "--help",
        'h',
        aliases: [],
        FlagCategory.Diagnostics,
        "Print help information.",
        static matchedName => matchedName == "-h"
            ? CliSpecialMode.HelpShort
            : CliSpecialMode.HelpLong);
}
