using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreFlag : IFlag<IgnoreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore",
        shortName: null,
        "--no-ignore",
        aliases: [],
        FlagCategory.Search,
        "Respect ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectIgnoreFiles(true);
            return null;
        });
}
