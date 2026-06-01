using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(2)]
internal readonly struct AfterContextFlag : IFlag<AfterContextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--after-context",
        'A',
        aliases: [],
        FlagCategory.Output,
        "Print NUM lines after each match.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseAfterContext(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
