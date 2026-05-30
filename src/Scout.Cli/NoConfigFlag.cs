namespace Scout;

internal readonly struct NoConfigFlag : IFlag<NoConfigFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-config",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Disable ripgrep configuration expansion.",
        static _ => null);
}
