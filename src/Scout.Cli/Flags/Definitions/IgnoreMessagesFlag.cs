using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreMessagesFlag : IFlag<IgnoreMessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-messages",
        shortName: null,
        "--no-ignore-messages",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print ignore-file diagnostics.",
        static lowArgs =>
        {
            lowArgs.SetIgnoreMessages(true);
            return null;
        });
}
