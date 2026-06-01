using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(60)]
internal readonly struct IgnoreMessagesFlag : IFlag<IgnoreMessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-messages",
        shortName: null,
        "--no-ignore-messages",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print ignore-file diagnostics.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetIgnoreMessages(matchedName != "--no-ignore-messages");
            return null;
        });
}
