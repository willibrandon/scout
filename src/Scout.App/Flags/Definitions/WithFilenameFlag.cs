using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct WithFilenameFlag : IFlag<WithFilenameFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--with-filename",
        'H',
        "--no-filename",
        aliases: [],
        FlagCategory.Output,
        "Print filename prefixes in matching output.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetWithFilename(matchedName != "--no-filename" && matchedName != "-I");
            return null;
        },
        negatedShortName: 'I');
}
