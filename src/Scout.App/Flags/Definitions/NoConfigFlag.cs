
namespace Scout.Flags.Definitions;

[FlagOrder(54)]
internal readonly struct NoConfigFlag : IFlag<NoConfigFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-config",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Disable Scout configuration expansion.",
        static _ => null);
}
