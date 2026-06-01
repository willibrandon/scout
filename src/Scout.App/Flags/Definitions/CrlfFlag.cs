using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(15)]
internal readonly struct CrlfFlag : IFlag<CrlfFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--crlf",
        shortName: null,
        "--no-crlf",
        aliases: [],
        FlagCategory.Matching,
        "Treat CRLF as a line terminator.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetCrlf(matchedName != "--no-crlf");
            return null;
        });
}
