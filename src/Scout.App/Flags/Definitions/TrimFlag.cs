
namespace Scout.Flags.Definitions;

[FlagOrder(89)]
internal readonly struct TrimFlag : IFlag<TrimFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--trim",
        shortName: null,
        "--no-trim",
        aliases: [],
        FlagCategory.Output,
        "Trim leading ASCII whitespace from printed lines.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetTrim(matchedName != "--no-trim");
            return null;
        });
}
