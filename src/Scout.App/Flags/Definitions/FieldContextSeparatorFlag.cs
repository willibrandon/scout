
namespace Scout.Flags.Definitions;

[FlagOrder(20)]
internal readonly struct FieldContextSeparatorFlag : IFlag<FieldContextSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--field-context-separator",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Set the field context separator.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseSeparator(value, matchedName, SeparatorKind.FieldContext, lowArgs, out ScoutError? error);
            return error;
        });
}
