namespace Scout;

internal readonly struct SearchZipFlag : IFlag<SearchZipFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--search-zip",
        'z',
        "--no-search-zip",
        aliases: [],
        FlagCategory.Search,
        "Search compressed files through preprocessors.",
        static lowArgs =>
        {
            lowArgs.SetSearchZip(true);
            return null;
        });
}
