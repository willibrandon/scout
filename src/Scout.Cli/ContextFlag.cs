namespace Scout;

internal readonly struct ContextFlag : IFlag<ContextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--context",
        'C',
        aliases: [],
        FlagCategory.Output,
        "Print NUM lines before and after each match.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseContext(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
