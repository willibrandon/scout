using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(71)]
internal readonly struct PassthruFlag : IFlag<PassthruFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--passthru",
        shortName: null,
        negatedName: null,
        aliases: ["--passthrough"],
        FlagCategory.Output,
        "Print both matching and non-matching lines.",
        static lowArgs =>
        {
            lowArgs.SetPassthru(true);
            return null;
        });
}
