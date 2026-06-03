
namespace Scout.Flags.Definitions;

[FlagOrder(63)]
internal readonly struct MessagesFlag : IFlag<MessagesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--messages",
        shortName: null,
        "--no-messages",
        aliases: [],
        FlagCategory.Diagnostics,
        "Print non-fatal search diagnostics.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetMessages(matchedName != "--no-messages");
            return null;
        });
}
