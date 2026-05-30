namespace Scout;

internal readonly struct NoFixedStringsFlag : IFlag<NoFixedStringsFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-fixed-strings",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Treat patterns as regular expressions.",
        static lowArgs =>
        {
            lowArgs.SetFixedStrings(false);
            return null;
        });
}
