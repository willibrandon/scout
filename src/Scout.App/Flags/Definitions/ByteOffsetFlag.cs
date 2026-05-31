using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct ByteOffsetFlag : IFlag<ByteOffsetFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--byte-offset",
        'b',
        "--no-byte-offset",
        aliases: [],
        FlagCategory.Output,
        "Print byte offsets with matching lines.",
        static lowArgs =>
        {
            lowArgs.SetByteOffset(true);
            return null;
        });
}
