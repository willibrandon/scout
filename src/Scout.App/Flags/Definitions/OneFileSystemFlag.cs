
namespace Scout.Flags.Definitions;

[FlagOrder(68)]
internal readonly struct OneFileSystemFlag : IFlag<OneFileSystemFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--one-file-system",
        shortName: null,
        "--no-one-file-system",
        aliases: [],
        FlagCategory.Search,
        "Stay on one file system during traversal.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetOneFileSystem(matchedName != "--no-one-file-system");
            return null;
        });
}
