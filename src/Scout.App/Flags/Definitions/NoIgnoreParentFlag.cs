using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoIgnoreParentFlag : IFlag<NoIgnoreParentFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-parent",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect ignore files in parent directories.",
        static lowArgs =>
        {
            lowArgs.SetRespectParentIgnoreFiles(false);
            return null;
        });
}
