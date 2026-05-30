namespace Scout;

internal readonly struct MaxColumnsFlag : IFlag<MaxColumnsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--max-columns",
        'M',
        aliases: [],
        FlagCategory.Output,
        "Omit lines longer than this limit.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseMaxColumns(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
