
namespace Scout.Flags.Definitions;

[FlagOrder(28)]
internal readonly struct GlobFlag : IFlag<GlobFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--glob",
        'g',
        aliases: [],
        FlagCategory.Search,
        "Include or exclude paths with a glob.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseGlob(value, matchedName, caseInsensitive: false, lowArgs, out ScoutError? error);
            return error;
        });
}
