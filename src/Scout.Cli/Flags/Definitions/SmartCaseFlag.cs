using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct SmartCaseFlag : IFlag<SmartCaseFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--smart-case",
        'S',
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Use smart case matching.",
        static lowArgs =>
        {
            lowArgs.SetCaseMode(CliCaseMode.Smart);
            return null;
        });
}
