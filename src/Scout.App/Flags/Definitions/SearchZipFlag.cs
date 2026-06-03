
namespace Scout.Flags.Definitions;

[FlagOrder(80)]
internal readonly struct SearchZipFlag : IFlag<SearchZipFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--search-zip",
        'z',
        "--no-search-zip",
        aliases: [],
        FlagCategory.Search,
        "Search compressed files through preprocessors.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetSearchZip(matchedName != "--no-search-zip");
            return null;
        });
}
