namespace Scout;

internal readonly struct NoIgnoreFileCaseInsensitiveFlag : IFlag<NoIgnoreFileCaseInsensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-file-case-insensitive",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Match ignore files case sensitively.",
        static lowArgs =>
        {
            lowArgs.SetIgnoreFileCaseInsensitive(false);
            return null;
        });
}
