using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(10)]
internal readonly struct ColumnFlag : IFlag<ColumnFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--column",
        shortName: null,
        "--no-column",
        aliases: [],
        FlagCategory.Output,
        "Print column numbers with matching lines.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetColumn(matchedName != "--no-column");
            return null;
        });
}
