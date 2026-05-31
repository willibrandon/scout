using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct RegexpFlag : IFlag<RegexpFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--regexp",
        'e',
        aliases: [],
        FlagCategory.Matching,
        "Add a search pattern.",
        static (lowArgs, value, flagName) =>
        {
            CliParser.ParsePattern(value, flagName, lowArgs, out ScoutError? error);
            return error;
        });
}
