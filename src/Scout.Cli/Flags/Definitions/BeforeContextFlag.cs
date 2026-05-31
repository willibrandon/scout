using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct BeforeContextFlag : IFlag<BeforeContextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--before-context",
        'B',
        aliases: [],
        FlagCategory.Output,
        "Print NUM lines before each match.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseBeforeContext(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
