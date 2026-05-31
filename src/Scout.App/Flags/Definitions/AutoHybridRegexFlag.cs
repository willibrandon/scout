using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct AutoHybridRegexFlag : IFlag<AutoHybridRegexFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--auto-hybrid-regex",
        shortName: null,
        "--no-auto-hybrid-regex",
        aliases: [],
        FlagCategory.Regex,
        "Automatically use hybrid regex matching when needed.",
        static lowArgs =>
        {
            lowArgs.SetAutoHybridRegex(true);
            return null;
        });
}
