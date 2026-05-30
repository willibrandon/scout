namespace Scout;

internal readonly struct NoIgnoreExcludeFlag : IFlag<NoIgnoreExcludeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-exclude",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Do not respect git exclude files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGitExcludeFiles(false);
            return null;
        });
}
