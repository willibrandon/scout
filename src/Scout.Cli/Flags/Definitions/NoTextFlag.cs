using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoTextFlag : IFlag<NoTextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-text",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Binary,
        "Do not force binary files to be searched as text.",
        static lowArgs =>
        {
            lowArgs.SetTextMode(false);
            return null;
        });
}
