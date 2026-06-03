
namespace Scout.Flags.Definitions;

[FlagOrder(34)]
internal readonly struct HyperlinkFormatFlag : IFlag<HyperlinkFormatFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--hyperlink-format",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Set the terminal hyperlink format.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseHyperlinkFormat(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
