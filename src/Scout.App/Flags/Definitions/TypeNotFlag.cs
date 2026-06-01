using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(91)]
internal readonly struct TypeNotFlag : IFlag<TypeNotFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--type-not",
        'T',
        aliases: [],
        FlagCategory.Search,
        "Exclude files matching a type.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseTypeChange(value, matchedName, CliTypeChangeKind.Negate, lowArgs, out ScoutError? error);
            return error;
        });
}
