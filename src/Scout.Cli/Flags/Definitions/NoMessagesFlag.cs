using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoMessagesFlag : IFlag<NoMessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-messages",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Suppress non-fatal search diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetMessages(false);
            return null;
        });
}
