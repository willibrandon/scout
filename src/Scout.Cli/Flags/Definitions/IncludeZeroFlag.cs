using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct IncludeZeroFlag : IFlag<IncludeZeroFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--include-zero",
        shortName: null,
        "--no-include-zero",
        aliases: [],
        FlagCategory.Output,
        "Include zero-count entries in count output.",
        static lowArgs =>
        {
            lowArgs.SetIncludeZero(true);
            return null;
        });
}
