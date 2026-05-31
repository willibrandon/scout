using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoAutoHybridRegexFlag : IFlag<NoAutoHybridRegexFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-auto-hybrid-regex",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Disable automatic hybrid regex matching.",
        static lowArgs =>
        {
            lowArgs.SetAutoHybridRegex(false);
            return null;
        });
}
