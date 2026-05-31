using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct TypeAddFlag : IFlag<TypeAddFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--type-add",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Add a file type definition.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseTypeChange(value, matchedName, CliTypeChangeKind.Add, lowArgs, out ScoutError? error);
            return error;
        });
}
