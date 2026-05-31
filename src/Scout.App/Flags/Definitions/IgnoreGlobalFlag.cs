using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IgnoreGlobalFlag : IFlag<IgnoreGlobalFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--ignore-global",
        shortName: null,
        "--no-ignore-global",
        aliases: [],
        FlagCategory.Search,
        "Respect global ignore files.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetRespectGlobalIgnoreFiles(matchedName != "--no-ignore-global");
            return null;
        });
}
