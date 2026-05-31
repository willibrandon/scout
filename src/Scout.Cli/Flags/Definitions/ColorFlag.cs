using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct ColorFlag : IFlag<ColorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--color",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Control when color is used.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParseColor(value, lowArgs, out ScoutError? error);
            return error;
        });
}
