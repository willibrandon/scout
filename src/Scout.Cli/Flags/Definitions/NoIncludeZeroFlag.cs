using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoIncludeZeroFlag : IFlag<NoIncludeZeroFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-include-zero",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Omit zero-count entries in count output.",
        static lowArgs =>
        {
            lowArgs.SetIncludeZero(false);
            return null;
        });
}
