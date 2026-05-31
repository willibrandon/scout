using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct PreFlag : IFlag<PreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.ValueWithNegatedSwitch(
        "--pre",
        shortName: null,
        negatedName: "--no-pre",
        aliases: [],
        FlagCategory.Search,
        "Run a preprocessor before searching.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParsePreprocessor(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        },
        static lowArgs =>
        {
            lowArgs.SetPreprocessor(null);
            return null;
        });
}
