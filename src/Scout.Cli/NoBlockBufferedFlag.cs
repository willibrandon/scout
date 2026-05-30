namespace Scout;

internal readonly struct NoBlockBufferedFlag : IFlag<NoBlockBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-block-buffered",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Use automatic stdout buffering instead of block buffering.",
        static lowArgs =>
        {
            lowArgs.SetBufferMode(CliBufferMode.Auto);
            return null;
        });
}
