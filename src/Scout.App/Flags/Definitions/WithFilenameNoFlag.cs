using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct WithFilenameNoFlag : IFlag<WithFilenameNoFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-filename",
        'I',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Never print filename prefixes in matching output.",
        static lowArgs =>
        {
            lowArgs.SetWithFilename(false);
            return null;
        });
}
