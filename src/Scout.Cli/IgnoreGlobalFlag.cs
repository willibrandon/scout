namespace Scout;

internal readonly struct IgnoreGlobalFlag : IFlag<IgnoreGlobalFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-global",
        shortName: null,
        "--no-ignore-global",
        aliases: [],
        FlagCategory.Search,
        "Respect global ignore files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGlobalIgnoreFiles(true);
            return null;
        });
}
