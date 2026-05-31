using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct BinaryFlag : IFlag<BinaryFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--binary",
        shortName: null,
        "--no-binary",
        aliases: [],
        FlagCategory.Binary,
        "Search binary files with suppression.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetSearchBinaryFiles(matchedName != "--no-binary");
            return null;
        });
}
