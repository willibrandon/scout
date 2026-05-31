using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoLineBufferedFlag : IFlag<NoLineBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-line-buffered",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Use automatic stdout buffering instead of line buffering.",
        static lowArgs =>
        {
            lowArgs.SetBufferMode(CliBufferMode.Auto);
            return null;
        });
}
