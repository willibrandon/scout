using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct TypeFlag : IFlag<TypeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--type",
        't',
        aliases: [],
        FlagCategory.Search,
        "Only search files matching a type.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseTypeChange(value, matchedName, CliTypeChangeKind.Select, lowArgs, out ScoutError? error);
            return error;
        });
}
