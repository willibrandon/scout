using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoHeadingFlag : IFlag<NoHeadingFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-heading",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Do not group matches under file headings.",
        static lowArgs =>
        {
            lowArgs.SetHeading(false);
            return null;
        });
}
