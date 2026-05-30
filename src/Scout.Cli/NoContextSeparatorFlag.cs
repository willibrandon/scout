namespace Scout;

internal readonly struct NoContextSeparatorFlag : IFlag<NoContextSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-context-separator",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Suppress context separators.",
        static lowArgs =>
        {
            lowArgs.SetContextSeparatorEnabled(false);
            return null;
        });
}
