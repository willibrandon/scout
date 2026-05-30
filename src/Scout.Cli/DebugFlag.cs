namespace Scout;

internal readonly struct DebugFlag : IFlag<DebugFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--debug",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Print debug diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetLoggingMode(CliLoggingMode.Debug);
            return null;
        });
}
