namespace Scout;

internal readonly struct NoLineNumberFlag : IFlag<NoLineNumberFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-line-number",
        'N',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Disable line numbers in matching output.",
        static lowArgs =>
        {
            lowArgs.SetLineNumber(false);
            return null;
        });
}
