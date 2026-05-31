using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct HeadingFlag : IFlag<HeadingFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--heading",
        shortName: null,
        "--no-heading",
        aliases: [],
        FlagCategory.Output,
        "Group matches under file headings.",
        static lowArgs =>
        {
            lowArgs.SetHeading(true);
            return null;
        });
}
