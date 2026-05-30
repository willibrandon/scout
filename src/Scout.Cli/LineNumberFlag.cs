namespace Scout;

internal readonly struct LineNumberFlag : IFlag<LineNumberFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--line-number",
        'n',
        "--no-line-number",
        aliases: [],
        FlagCategory.Output,
        "Print line numbers with matching lines.",
        static lowArgs =>
        {
            lowArgs.SetLineNumber(true);
            return null;
        });
}
