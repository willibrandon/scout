using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(42)]
internal readonly struct LineBufferedFlag : IFlag<LineBufferedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--line-buffered",
        shortName: null,
        "--no-line-buffered",
        aliases: [],
        FlagCategory.Output,
        "Use line buffering for stdout.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetBufferMode(matchedName == "--no-line-buffered" ? CliBufferMode.Auto : CliBufferMode.Line);
            return null;
        });
}
