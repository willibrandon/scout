namespace Scout;

internal readonly struct NoFilenameFlag : IFlag<NoFilenameFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-filename",
        'I',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Suppress filename prefixes in matching output.",
        static lowArgs =>
        {
            lowArgs.SetWithFilename(false);
            return null;
        });
}
