namespace Scout;

internal readonly struct MessagesFlag : IFlag<MessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--messages",
        shortName: null,
        "--no-messages",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print non-fatal search diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetMessages(true);
            return null;
        });
}
