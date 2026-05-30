namespace Scout;

internal readonly struct NoIgnoreMessagesFlag : IFlag<NoIgnoreMessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-ignore-messages",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Suppress ignore-file diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetIgnoreMessages(false);
            return null;
        });
}
