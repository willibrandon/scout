using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(36)]
internal readonly struct IgnoreCaseFlag : IFlag<IgnoreCaseFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--ignore-case",
        'i',
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Search case insensitively.",
        static lowArgs =>
        {
            lowArgs.SetCaseMode(CliCaseMode.Insensitive);
            return null;
        });
}
