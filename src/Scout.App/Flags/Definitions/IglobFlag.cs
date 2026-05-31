using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IglobFlag : IFlag<IglobFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--iglob",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Include or exclude paths with a case-insensitive glob.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseGlob(value, matchedName, caseInsensitive: true, lowArgs, out ScoutError? error);
            return error;
        });
}
