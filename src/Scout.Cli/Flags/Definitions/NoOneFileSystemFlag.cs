using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoOneFileSystemFlag : IFlag<NoOneFileSystemFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-one-file-system",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Allow traversal across file-system boundaries.",
        static lowArgs =>
        {
            lowArgs.SetOneFileSystem(false);
            return null;
        });
}
