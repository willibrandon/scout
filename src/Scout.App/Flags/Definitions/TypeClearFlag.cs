using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(93)]
internal readonly struct TypeClearFlag : IFlag<TypeClearFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--type-clear",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Clear a file type definition.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseTypeChange(value, matchedName, CliTypeChangeKind.Clear, lowArgs, out ScoutError? error);
            return error;
        });
}
