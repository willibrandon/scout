using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(17)]
internal readonly struct DfaSizeLimitFlag : IFlag<DfaSizeLimitFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--dfa-size-limit",
        shortName: null,
        aliases: [],
        FlagCategory.Regex,
        "Set the DFA size limit.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseDfaSizeLimit(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
