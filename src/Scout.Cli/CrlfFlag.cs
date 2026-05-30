namespace Scout;

internal readonly struct CrlfFlag : IFlag<CrlfFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--crlf",
        shortName: null,
        "--no-crlf",
        aliases: [],
        FlagCategory.Matching,
        "Treat CRLF as a line terminator.",
        static lowArgs =>
        {
            lowArgs.SetCrlf(true);
            return null;
        });
}
