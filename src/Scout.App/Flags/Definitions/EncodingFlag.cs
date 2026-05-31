using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct EncodingFlag : IFlag<EncodingFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.ValueWithNegatedSwitch(
        "--encoding",
        'E',
        "--no-encoding",
        aliases: [],
        FlagCategory.Search,
        "Set the input encoding.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseEncoding(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        },
        static lowArgs =>
        {
            lowArgs.SetEncodingMode(CliEncodingMode.Auto);
            return null;
        });
}
