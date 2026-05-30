namespace Scout;

internal readonly struct LineBufferedFlag : IFlag<LineBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--line-buffered",
        shortName: null,
        "--no-line-buffered",
        aliases: [],
        FlagCategory.Output,
        "Use line buffering for stdout.",
        static lowArgs =>
        {
            lowArgs.SetBufferMode(CliBufferMode.Line);
            return null;
        });
}
