namespace Scout;

internal readonly struct NoIgnoreFlag : IFlag<NoIgnoreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectIgnoreFiles(false);
            return null;
        });
}
