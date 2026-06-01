using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(87)]
internal readonly struct ThreadsFlag : IFlag<ThreadsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--threads",
        'j',
        aliases: [],
        FlagCategory.Search,
        "Set the approximate number of search threads.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseThreads(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
