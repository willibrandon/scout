namespace Scout;

internal readonly struct MultilineFlag : IFlag<MultilineFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--multiline",
        'U',
        "--no-multiline",
        aliases: [],
        FlagCategory.Regex,
        "Permit matches to span line terminators.",
        static lowArgs =>
        {
            lowArgs.SetMultiline(true);
            return null;
        });
}
