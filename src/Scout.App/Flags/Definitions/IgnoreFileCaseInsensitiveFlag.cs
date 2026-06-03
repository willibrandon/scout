
namespace Scout.Flags.Definitions;

[FlagOrder(38)]
internal readonly struct IgnoreFileCaseInsensitiveFlag : IFlag<IgnoreFileCaseInsensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-file-case-insensitive",
        shortName: null,
        "--no-ignore-file-case-insensitive",
        aliases: [],
        FlagCategory.Search,
        "Match ignore files case insensitively.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetIgnoreFileCaseInsensitive(matchedName != "--no-ignore-file-case-insensitive");
            return null;
        });
}
