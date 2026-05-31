using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct MaxCountFlag : IFlag<MaxCountFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--max-count",
        'm',
        aliases: [],
        FlagCategory.Search,
        "Limit matching lines per file.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseMaxCount(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
