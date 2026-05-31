using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct ColorsFlag : IFlag<ColorsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--colors",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Configure color styles.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseColorSpec(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
