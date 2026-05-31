using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoUnicodeFlag : IFlag<NoUnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-unicode",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Regex,
        "Disable Unicode regex mode.",
        static lowArgs =>
        {
            lowArgs.SetUnicode(false);
            return null;
        });
}
