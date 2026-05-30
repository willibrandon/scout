namespace Scout;

internal readonly struct TypeListFlag : IFlag<TypeListFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--type-list",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Print supported file type definitions.",
        static lowArgs =>
        {
            lowArgs.SetTypeList(true);
            return null;
        });
}
