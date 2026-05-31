using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoBinaryFlag : IFlag<NoBinaryFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-binary",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Binary,
        "Do not search binary files with suppression.",
        static lowArgs =>
        {
            lowArgs.SetSearchBinaryFiles(false);
            return null;
        });
}
