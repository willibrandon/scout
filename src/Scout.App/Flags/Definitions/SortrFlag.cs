
namespace Scout.Flags.Definitions;

[FlagOrder(83)]
internal readonly struct SortrFlag : IFlag<SortrFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--sortr",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Sort results descending.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSort(value, matchedName, reverse: true, lowArgs, out ScoutError? error);
            return error;
        });
}
