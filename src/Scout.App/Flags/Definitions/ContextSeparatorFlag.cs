using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct ContextSeparatorFlag : IFlag<ContextSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--context-separator",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Set the context group separator.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSeparator(value, matchedName, SeparatorKind.Context, lowArgs, out ScoutError? error);
            return error;
        });
}
