using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct BlockBufferedFlag : IFlag<BlockBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--block-buffered",
        shortName: null,
        "--no-block-buffered",
        aliases: [],
        FlagCategory.Output,
        "Use block buffering for stdout.",
        static lowArgs =>
        {
            lowArgs.SetBufferMode(CliBufferMode.Block);
            return null;
        });
}
