using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(33)]
internal readonly struct HostnameBinFlag : IFlag<HostnameBinFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--hostname-bin",
        shortName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Set the hostname command for trace logs.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseHostnameBin(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
