namespace Scout;

internal readonly struct NoHiddenFlag : IFlag<NoHiddenFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-hidden",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not search hidden files and directories.",
        static lowArgs =>
        {
            lowArgs.SetIncludeHidden(false);
            return null;
        });
}
