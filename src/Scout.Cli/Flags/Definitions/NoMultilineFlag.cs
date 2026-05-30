namespace Scout.Flags.Definitions;

internal readonly struct NoMultilineFlag : IFlag<NoMultilineFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-multiline",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Disable multiline matching.",
        static lowArgs =>
        {
            lowArgs.SetMultiline(false);
            return null;
        });
}
