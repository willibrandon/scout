using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(19)]
internal readonly struct EngineFlag : IFlag<EngineFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--engine",
        shortName: null,
        aliases: [],
        FlagCategory.Regex,
        "Select the regex engine.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParseRegexEngine(value, lowArgs, out ScoutError? error);
            return error;
        });
}
