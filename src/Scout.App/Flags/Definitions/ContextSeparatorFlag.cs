using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct ContextSeparatorFlag : IFlag<ContextSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.ValueWithNegatedSwitch(
        "--context-separator",
        shortName: null,
        negatedName: "--no-context-separator",
        aliases: [],
        FlagCategory.Output,
        "Set the context group separator.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSeparator(value, matchedName, SeparatorKind.Context, lowArgs, out ScoutError? error);
            return error;
        },
        static lowArgs =>
        {
            lowArgs.SetContextSeparatorEnabled(false);
            return null;
        });
}
