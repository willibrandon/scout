using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct WithFilenameFlag : IFlag<WithFilenameFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--with-filename",
        'H',
        "--no-filename",
        aliases: [],
        FlagCategory.Output,
        "Print filename prefixes in matching output.",
        static lowArgs =>
        {
            lowArgs.SetWithFilename(true);
            return null;
        });
}
