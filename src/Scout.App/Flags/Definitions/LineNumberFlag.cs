using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct LineNumberFlag : IFlag<LineNumberFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--line-number",
        'n',
        "--no-line-number",
        aliases: [],
        FlagCategory.Output,
        "Print line numbers with matching lines.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetLineNumber(matchedName != "--no-line-number" && matchedName != "-N");
            return null;
        },
        negatedShortName: 'N');
}
