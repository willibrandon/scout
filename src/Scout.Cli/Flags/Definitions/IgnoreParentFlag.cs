using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreParentFlag : IFlag<IgnoreParentFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-parent",
        shortName: null,
        "--no-ignore-parent",
        aliases: [],
        FlagCategory.Search,
        "Respect ignore files in parent directories.",
        static lowArgs =>
        {
            lowArgs.SetRespectParentIgnoreFiles(true);
            return null;
        });
}
