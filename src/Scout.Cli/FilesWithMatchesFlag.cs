namespace Scout;

internal readonly struct FilesWithMatchesFlag : IFlag<FilesWithMatchesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--files-with-matches",
        'l',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Print only the paths with at least one match.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.FilesWithMatches);
            return null;
        });
}
