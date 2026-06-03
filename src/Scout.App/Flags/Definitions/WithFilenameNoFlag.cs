
namespace Scout.Flags.Definitions;

[FlagOrder(99)]
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
