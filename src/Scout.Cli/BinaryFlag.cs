namespace Scout;

internal readonly struct BinaryFlag : IFlag<BinaryFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--binary",
        shortName: null,
        "--no-binary",
        aliases: [],
        FlagCategory.Binary,
        "Search binary files with suppression.",
        static lowArgs =>
        {
            lowArgs.SetSearchBinaryFiles(true);
            return null;
        });
}
