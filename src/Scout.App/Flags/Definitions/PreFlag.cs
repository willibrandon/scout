using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct PreFlag : IFlag<PreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--pre",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Run a preprocessor before searching.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParsePreprocessor(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
