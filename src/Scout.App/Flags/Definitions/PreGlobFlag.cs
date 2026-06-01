using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(75)]
internal readonly struct PreGlobFlag : IFlag<PreGlobFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--pre-glob",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Limit preprocessing with a glob.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParsePreprocessorGlob(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
