using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct CaseSensitiveFlag : IFlag<CaseSensitiveFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--case-sensitive",
        's',
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Search case sensitively.",
        static lowArgs =>
        {
            lowArgs.SetCaseMode(CliCaseMode.Sensitive);
            return null;
        });
}
