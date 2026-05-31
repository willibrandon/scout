using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct FieldMatchSeparatorFlag : IFlag<FieldMatchSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--field-match-separator",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Set the field match separator.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSeparator(value, matchedName, SeparatorKind.FieldMatch, lowArgs, out ScoutError? error);
            return error;
        });
}
