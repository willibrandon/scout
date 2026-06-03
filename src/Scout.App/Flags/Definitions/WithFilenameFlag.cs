
namespace Scout.Flags.Definitions;

[FlagOrder(98)]
internal readonly struct WithFilenameFlag : IFlag<WithFilenameFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--with-filename",
        'H',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Print filename prefixes in matching output.",
        static lowArgs =>
        {
            lowArgs.SetWithFilename(true);
            return null;
        });
}
