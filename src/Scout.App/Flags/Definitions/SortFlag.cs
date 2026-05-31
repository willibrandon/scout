using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct SortFlag : IFlag<SortFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--sort",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Sort results ascending.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSort(value, matchedName, reverse: false, lowArgs, out ScoutError? error);
            return error;
        });
}
