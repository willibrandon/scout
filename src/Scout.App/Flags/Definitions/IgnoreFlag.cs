using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(55)]
internal readonly struct IgnoreFlag : IFlag<IgnoreFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore",
        shortName: null,
        "--no-ignore",
        aliases: [],
        FlagCategory.Search,
        "Respect ignore files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectIgnoreFiles(matchedName != "--no-ignore");
            return null;
        });
}
