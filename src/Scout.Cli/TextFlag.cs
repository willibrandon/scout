namespace Scout;

internal readonly struct TextFlag : IFlag<TextFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--text",
        'a',
        "--no-text",
        aliases: [],
        FlagCategory.Binary,
        "Search binary files as if they were text.",
        static lowArgs =>
        {
            lowArgs.SetTextMode(true);
            return null;
        });
}
