namespace Scout;

internal readonly struct OneFileSystemFlag : IFlag<OneFileSystemFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--one-file-system",
        shortName: null,
        "--no-one-file-system",
        aliases: [],
        FlagCategory.Search,
        "Stay on one file system during traversal.",
        static lowArgs =>
        {
            lowArgs.SetOneFileSystem(true);
            return null;
        });
}
