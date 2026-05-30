namespace Scout;

internal readonly struct NullFlag : IFlag<NullFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--null",
        '0',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Terminate printed paths with NUL.",
        static lowArgs =>
        {
            lowArgs.SetNullPathTerminator(true);
            return null;
        });
}
