namespace Scout;

internal readonly struct NoGlobCaseInsensitiveFlag : IFlag<NoGlobCaseInsensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-glob-case-insensitive",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Match glob patterns case sensitively.",
        static lowArgs =>
        {
            lowArgs.SetGlobCaseInsensitive(false);
            return null;
        });
}
