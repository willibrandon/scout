namespace Scout;

internal readonly struct GlobCaseInsensitiveFlag : IFlag<GlobCaseInsensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--glob-case-insensitive",
        shortName: null,
        "--no-glob-case-insensitive",
        aliases: [],
        FlagCategory.Search,
        "Match glob patterns case insensitively.",
        static lowArgs =>
        {
            lowArgs.SetGlobCaseInsensitive(true);
            return null;
        });
}
