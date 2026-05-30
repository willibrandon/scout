namespace Scout;

internal readonly struct NoSearchZipFlag : IFlag<NoSearchZipFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-search-zip",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not search compressed files through preprocessors.",
        static lowArgs =>
        {
            lowArgs.SetSearchZip(false);
            return null;
        });
}
