namespace Scout;

internal readonly struct QuietFlag : IFlag<QuietFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--quiet",
        'q',
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Suppress stdout output.",
        static lowArgs =>
        {
            lowArgs.SetQuiet(true);
            return null;
        });
}
