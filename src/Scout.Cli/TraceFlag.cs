namespace Scout;

internal readonly struct TraceFlag : IFlag<TraceFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--trace",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Print trace diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetLoggingMode(CliLoggingMode.Trace);
            return null;
        });
}
