namespace Scout;

internal readonly struct IgnoreExcludeFlag : IFlag<IgnoreExcludeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-exclude",
        shortName: null,
        "--no-ignore-exclude",
        aliases: [],
        FlagCategory.Search,
        "Respect git exclude files.",
        static lowArgs =>
        {
            lowArgs.SetRespectGitExcludeFiles(true);
            return null;
        });
}
