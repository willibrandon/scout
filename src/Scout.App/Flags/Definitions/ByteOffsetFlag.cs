
namespace Scout.Flags.Definitions;

[FlagOrder(6)]
internal readonly struct ByteOffsetFlag : IFlag<ByteOffsetFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--byte-offset",
        'b',
        "--no-byte-offset",
        aliases: [],
        FlagCategory.Output,
        "Print byte offsets with matching lines.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetByteOffset(matchedName != "--no-byte-offset");
            return null;
        });
}
