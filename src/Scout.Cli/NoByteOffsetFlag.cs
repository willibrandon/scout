namespace Scout;

internal readonly struct NoByteOffsetFlag : IFlag<NoByteOffsetFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-byte-offset",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Disable byte offsets in matching output.",
        static lowArgs =>
        {
            lowArgs.SetByteOffset(false);
            return null;
        });
}
