using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoCrlfFlag : IFlag<NoCrlfFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-crlf",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Do not treat CRLF as a line terminator.",
        static lowArgs =>
        {
            lowArgs.SetCrlf(false);
            return null;
        });
}
