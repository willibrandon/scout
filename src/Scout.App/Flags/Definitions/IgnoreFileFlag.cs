using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(37)]
internal readonly struct IgnoreFileFlag : IFlag<IgnoreFileFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--ignore-file",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Add an explicit ignore file.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseIgnoreFile(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
