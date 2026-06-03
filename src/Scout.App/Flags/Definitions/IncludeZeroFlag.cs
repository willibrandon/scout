
namespace Scout.Flags.Definitions;

[FlagOrder(39)]
internal readonly struct IncludeZeroFlag : IFlag<IncludeZeroFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--include-zero",
        shortName: null,
        "--no-include-zero",
        aliases: [],
        FlagCategory.Output,
        "Include zero-count entries in count output.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetIncludeZero(matchedName != "--no-include-zero");
            return null;
        });
}
