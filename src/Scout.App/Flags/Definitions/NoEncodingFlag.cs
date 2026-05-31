using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoEncodingFlag : IFlag<NoEncodingFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-encoding",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Reset input encoding detection to automatic mode.",
        static lowArgs =>
        {
            lowArgs.SetEncodingMode(CliEncodingMode.Auto);
            return null;
        });
}
