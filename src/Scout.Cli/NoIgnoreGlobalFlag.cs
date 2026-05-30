namespace Scout;

internal readonly struct NoIgnoreGlobalFlag : IFlag<NoIgnoreGlobalFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-global",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect global ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGlobalIgnoreFiles(false);
            return null;
        });
}
