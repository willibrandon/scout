using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct BlockBufferedFlag : IFlag<BlockBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--block-buffered",
        shortName: null,
        "--no-block-buffered",
        aliases: [],
        FlagCategory.Output,
        "Use block buffering for stdout.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetBufferMode(matchedName == "--no-block-buffered" ? CliBufferMode.Auto : CliBufferMode.Block);
            return null;
        });
}
