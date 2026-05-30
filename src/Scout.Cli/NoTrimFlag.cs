namespace Scout;

internal readonly struct NoTrimFlag : IFlag<NoTrimFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-trim",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Do not trim leading ASCII whitespace.",
        static lowArgs =>
        {
            lowArgs.SetTrim(false);
            return null;
        });
}
