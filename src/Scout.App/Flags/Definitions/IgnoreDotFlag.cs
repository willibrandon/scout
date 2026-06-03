
namespace Scout.Flags.Definitions;

[FlagOrder(56)]
internal readonly struct IgnoreDotFlag : IFlag<IgnoreDotFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-dot",
        shortName: null,
        "--no-ignore-dot",
        aliases: [],
        FlagCategory.Search,
        "Respect .ignore and .rgignore files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectDotIgnoreFiles(matchedName != "--no-ignore-dot");
            return null;
        });
}
